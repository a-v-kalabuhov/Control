using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using ClosedXML.Excel;
using System.Text;

namespace Wintime.Control.Core.Services.Reports;

public class ReportService : IReportService
{
    private readonly ControlDbContext _context;
    private readonly ILogger<ReportService> _logger;

    public ReportService(ControlDbContext context, ILogger<ReportService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Отчёт "Картина рабочего дня"
    /// </summary>
    public async Task<DailyReportDto> GetDailyReportAsync(DateTime date, Guid? immId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating daily report for {Date}, IMM: {ImmId}", date, immId);

        var report = new DailyReportDto
        {
            Date = date.Date,
            ImmData = new List<DailyReportImmItemDto>()
        };

        // Получаем все активные ТПА
        var immQuery = _context.Imms
            .Include(i => i.Template)
            .Where(i => i.IsActive)
            .AsQueryable();

        if (immId.HasValue)
            immQuery = immQuery.Where(i => i.Id == immId.Value);

        var imms = await immQuery.ToListAsync(ct);

        // Границы отчётного периода (сутки)
        var periodStart = date.Date;
        var periodEnd = date.Date.AddDays(1);

        foreach (var imm in imms)
        {
            var item = await GenerateDailyImmItemAsync(imm, periodStart, periodEnd, ct);
            if (item != null)
                report.ImmData.Add(item);
        }

        return report;
    }

    /// <summary>
    /// Генерация данных по одному ТПА за день
    /// </summary>
    private async Task<DailyReportImmItemDto?> GenerateDailyImmItemAsync(Imm imm, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        // Получаем события за период
        var events = await _context.Events
            .Where(e => e.ImmId == imm.Id && e.StartTime >= periodStart && e.StartTime < periodEnd)
            .Include(e => e.Reason)
            .ToListAsync(ct);

        // Получаем телеметрию (статусы) за период
        var statusTelemetry = await _context.Telemetry
            .Where(t => t.ImmId == imm.Id && t.ParameterName == "status" && t.Timestamp >= periodStart && t.Timestamp < periodEnd)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(ct);

        // Получаем циклы за период
        var cycleTelemetry = await _context.Telemetry
            .Where(t => t.ImmId == imm.Id && t.ParameterName == "cycles" && t.Timestamp >= periodStart && t.Timestamp < periodEnd)
            .OrderBy(t => t.Timestamp)
            .ToListAsync(ct);

        // Получаем активное задание за период
        var task = await _context.Tasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(t => t.ImmId == imm.Id && t.StartedAt >= periodStart && t.StartedAt < periodEnd, ct);

        // Расчёт времени работы и простоев
        var workTimeSeconds = 0;
        var downtimeSeconds = 0;
        var offlineSeconds = 0;
        var totalCycles = 0;
        var cycleTimeSum = 0m;
        var cycleTimeCount = 0;

        // Анализируем статусы из телеметрии
        DateTime? lastStatusTime = periodStart;
        string? lastStatus = null;

        foreach (var status in statusTelemetry)
        {
            if (lastStatusTime.HasValue && lastStatus != null)
            {
                var duration = (int)(status.Timestamp - lastStatusTime.Value).TotalSeconds;

                switch (lastStatus?.ToLower())
                {
                    case "auto":
                        workTimeSeconds += duration;
                        break;
                    case "manual":
                    case "setup":
                        downtimeSeconds += duration;
                        break;
                    case "offline":
                        offlineSeconds += duration;
                        break;
                    case "alarm":
                        downtimeSeconds += duration;
                        break;
                }
            }

            lastStatusTime = status.Timestamp;
            lastStatus = status.ValueText;
        }

        // Последний интервал до конца периода
        if (lastStatusTime.HasValue && lastStatus != null && lastStatusTime.Value < periodEnd)
        {
            var duration = (int)(periodEnd - lastStatusTime.Value).TotalSeconds;

            switch (lastStatus?.ToLower())
            {
                case "auto":
                    workTimeSeconds += duration;
                    break;
                case "manual":
                case "setup":
                    downtimeSeconds += duration;
                    break;
                case "offline":
                    offlineSeconds += duration;
                    break;
                case "alarm":
                    downtimeSeconds += duration;
                    break;
            }
        }

        // Считаем циклы
        if (cycleTelemetry.Any())
        {
            var firstCycle = cycleTelemetry.First().ValueNumeric ?? 0;
            var lastCycle = cycleTelemetry.Last().ValueNumeric ?? 0;
            totalCycles = (int)(lastCycle - firstCycle);

            // Среднее время цикла
            var cycleTimeData = await _context.Telemetry
                .Where(t => t.ImmId == imm.Id && t.ParameterName == "cycle_time" && t.Timestamp >= periodStart && t.Timestamp < periodEnd)
                .ToListAsync(ct);

            if (cycleTimeData.Any())
            {
                cycleTimeSum = cycleTimeData.Average(t => t.ValueNumeric ?? 0);
                cycleTimeCount = cycleTimeData.Count;
            }
        }

        // Детализация простоев по причинам
        var downtimeDetails = new List<DowntimeDetailDto>();
        var downtimeEvents = events.Where(e => e.EventType == Core.Enums.EventType.Downtime).ToList();

        foreach (var evt in downtimeEvents)
        {
            var existing = downtimeDetails.FirstOrDefault(d => d.ReasonName == evt.ReasonName);
            if (existing != null)
            {
                existing.DurationSeconds += evt.DurationSeconds;
            }
            else
            {
                downtimeDetails.Add(new DowntimeDetailDto
                {
                    ReasonName = evt.ReasonName ?? "Неизвестно",
                    DurationSeconds = evt.DurationSeconds
                });
            }
        }

        // Расчёт расхода сырья (если есть задание с пресс-формой)
        decimal rawMaterialKg = 0;
        string? moldName = null;

        if (task?.Mold != null)
        {
            moldName = task.Mold.Name;
            var totalWeightGrams = task.Mold.PartWeightGrams + task.Mold.RunnerWeightGrams;
            rawMaterialKg = (totalWeightGrams * totalCycles * task.Mold.Cavities) / 1000m;
        }

        // Эффективность
        var efficiency = workTimeSeconds + downtimeSeconds > 0
            ? (decimal)workTimeSeconds / (workTimeSeconds + downtimeSeconds) * 100
            : 0;

        return new DailyReportImmItemDto
        {
            ImmId = imm.Id,
            ImmName = imm.Name,
            MoldName = moldName,
            PlanQuantity = task?.PlanQuantity ?? 0,
            ActualQuantity = totalCycles * (task?.Mold?.Cavities ?? 0),
            CycleCount = totalCycles,
            WorkTimeSeconds = workTimeSeconds,
            DowntimeSeconds = downtimeSeconds,
            OfflineSeconds = offlineSeconds,
            AvgCycleTime = cycleTimeCount > 0 ? cycleTimeSum / cycleTimeCount : 0,
            Efficiency = efficiency,
            RawMaterialKg = rawMaterialKg,
            DowntimeDetails = downtimeDetails
        };
    }

    /// <summary>
    /// Отчёт "Производительность оборудования"
    /// </summary>
    public async Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo, List<Guid>? immIds = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating equipment report from {From} to {To}", dateFrom, dateTo);

        var report = new EquipmentReportDto
        {
            DateFrom = dateFrom.Date,
            DateTo = dateTo.Date,
            ImmData = new List<EquipmentReportImmItemDto>()
        };

        var immQuery = _context.Imms
            .Include(i => i.Template)
            .Where(i => i.IsActive)
            .AsQueryable();

        if (immIds != null && immIds.Any())
            immQuery = immQuery.Where(i => immIds.Contains(i.Id));

        var imms = await immQuery.ToListAsync(ct);
        var periodEnd = dateTo.Date.AddDays(1);

        foreach (var imm in imms)
        {
            var events = await _context.Events
                .Where(e => e.ImmId == imm.Id && e.StartTime >= dateFrom && e.StartTime < periodEnd)
                .ToListAsync(ct);

            var statusTelemetry = await _context.Telemetry
                .Where(t => t.ImmId == imm.Id && t.ParameterName == "status" && t.Timestamp >= dateFrom && t.Timestamp < periodEnd)
                .OrderBy(t => t.Timestamp)
                .ToListAsync(ct);

            var cycleTelemetry = await _context.Telemetry
                .Where(t => t.ImmId == imm.Id && t.ParameterName == "cycles" && t.Timestamp >= dateFrom && t.Timestamp < periodEnd)
                .ToListAsync(ct);

            // Расчёт метрик (аналогично дневному отчёту)
            var workTimeSeconds = 0;
            var downtimeSeconds = 0;
            var totalCycles = 0;

            if (cycleTelemetry.Any())
            {
                totalCycles = (int)((cycleTelemetry.Last().ValueNumeric ?? 0) - (cycleTelemetry.First().ValueNumeric ?? 0));
            }

            // Упрощённый расчёт для периода
            var totalSeconds = (int)(periodEnd - dateFrom).TotalSeconds;
            var downtimeEvents = events.Where(e => e.EventType == Core.Enums.EventType.Downtime || e.EventType == Core.Enums.EventType.Alarm).ToList();
            downtimeSeconds = downtimeEvents.Sum(e => e.DurationSeconds);
            workTimeSeconds = Math.Max(0, totalSeconds - downtimeSeconds);

            var efficiency = workTimeSeconds + downtimeSeconds > 0
                ? (decimal)workTimeSeconds / (workTimeSeconds + downtimeSeconds) * 100
                : 0;

            report.ImmData.Add(new EquipmentReportImmItemDto
            {
                ImmId = imm.Id,
                ImmName = imm.Name,
                TotalWorkSeconds = workTimeSeconds,
                TotalDowntimeSeconds = downtimeSeconds,
                TotalCycles = totalCycles,
                AvgEfficiency = efficiency
            });
        }

        return report;
    }

    /// <summary>
    /// Отчёт "Активы цеха"
    /// </summary>
    public async Task<AssetsReportDto> GetAssetsReportAsync(DateTime dateFrom, DateTime dateTo, string reportType, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating assets report. Type: {Type}, From: {From}, To: {To}", reportType, dateFrom, dateTo);

        var report = new AssetsReportDto
        {
            DateFrom = dateFrom.Date,
            DateTo = dateTo.Date,
            ReportType = reportType
        };

        var periodEnd = dateTo.Date.AddDays(1);

        if (reportType.Equals("Molds", StringComparison.OrdinalIgnoreCase))
        {
            var molds = await _context.Molds
                .Where(m => m.IsActive)
                .ToListAsync(ct);

            report.MoldData = new List<AssetsMoldItemDto>();

            foreach (var mold in molds)
            {
                var usages = await _context.MoldUsages
                    .Where(mu => mu.MoldId == mold.Id && mu.StartTime >= dateFrom && mu.StartTime < periodEnd)
                    .ToListAsync(ct);

                var totalCycles = usages.Sum(mu => mu.CyclesCount);
                var totalSeconds = usages.Sum(mu => (mu.EndTime ?? DateTime.UtcNow) - mu.StartTime).TotalSeconds;
                var workHours = (decimal)totalSeconds / 3600;

                report.MoldData.Add(new AssetsMoldItemDto
                {
                    MoldId = mold.Id,
                    MoldName = mold.Name,
                    TotalCycles = totalCycles,
                    WorkHours = workHours,
                    RemainingResource = mold.MaxResourceCycles - totalCycles
                });
            }
        }
        else if (reportType.Equals("Personnel", StringComparison.OrdinalIgnoreCase))
        {
            var personnel = await _context.Users
                .Where(u => u.IsActive && u.Role == Core.Enums.UserRole.Adjuster)
                .ToListAsync(ct);

            report.PersonnelData = new List<AssetsPersonnelItemDto>();

            foreach (var person in personnel)
            {
                var tasks = await _context.Tasks
                    .Where(t => t.PersonnelId == person.Id && t.StartedAt >= dateFrom && t.StartedAt < periodEnd && t.Status >= Core.Enums.TaskStatus.Completed)
                    .ToListAsync(ct);

                var completedTasks = tasks.Count;
                var totalWorkSeconds = tasks
                    .Where(t => t.StartedAt.HasValue && t.CompletedAt.HasValue)
                    .Sum(t => (int)(t.CompletedAt!.Value - t.StartedAt!.Value).TotalSeconds);

                var setupEvents = await _context.Events
                    .Where(e => e.PersonnelId == person.Id && e.EventType == Core.Enums.EventType.Setup && e.StartTime >= dateFrom && e.StartTime < periodEnd)
                    .ToListAsync(ct);

                var avgSetupTime = setupEvents.Any()
                    ? (decimal)setupEvents.Average(e => e.DurationSeconds)
                    : 0;

                report.PersonnelData.Add(new AssetsPersonnelItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = completedTasks,
                    TotalWorkSeconds = totalWorkSeconds,
                    AvgSetupTime = avgSetupTime
                });
            }
        }

        return report;
    }

    /// <summary>
    /// Экспорт в Excel (ClosedXML)
    /// </summary>
    public async Task<byte[]> ExportToExcelAsync<T>(T data, string reportType, CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add($"Report_{reportType}");

        var row = 1;
        var col = 1;

        // Заголовок
        worksheet.Cell(row, col).Value = $"Отчёт: {reportType}";
        worksheet.Cell(row, col).Style.Font.Bold = true;
        worksheet.Cell(row, col).Style.Font.FontSize = 14;
        row += 2;

        // Динамическое заполнение в зависимости от типа отчёта
        if (data is DailyReportDto dailyReport)
        {
            // Заголовки таблицы
            var headers = new[] { "ТПА", "Пресс-форма", "План", "Факт", "Циклы", "Время работы (ч)", "Простой (ч)", "Эффективность %", "Сырьё (кг)" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(row, i + 1).Value = headers[i];
                worksheet.Cell(row, i + 1).Style.Font.Bold = true;
            }
            row++;

            // Данные
            foreach (var item in dailyReport.ImmData)
            {
                worksheet.Cell(row, 1).Value = item.ImmName;
                worksheet.Cell(row, 2).Value = item.MoldName;
                worksheet.Cell(row, 3).Value = item.PlanQuantity;
                worksheet.Cell(row, 4).Value = item.ActualQuantity;
                worksheet.Cell(row, 5).Value = item.CycleCount;
                worksheet.Cell(row, 6).Value = Math.Round(item.WorkTimeSeconds / 3600m, 2);
                worksheet.Cell(row, 7).Value = Math.Round(item.DowntimeSeconds / 3600m, 2);
                worksheet.Cell(row, 8).Value = Math.Round(item.Efficiency, 2);
                worksheet.Cell(row, 9).Value = Math.Round(item.RawMaterialKg, 2);
                row++;
            }
        }
        else if (data is EquipmentReportDto equipmentReport)
        {
            var headers = new[] { "ТПА", "Время работы (ч)", "Простой (ч)", "Циклы", "Эффективность %" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(row, i + 1).Value = headers[i];
                worksheet.Cell(row, i + 1).Style.Font.Bold = true;
            }
            row++;

            foreach (var item in equipmentReport.ImmData)
            {
                worksheet.Cell(row, 1).Value = item.ImmName;
                worksheet.Cell(row, 2).Value = Math.Round(item.TotalWorkSeconds / 3600m, 2);
                worksheet.Cell(row, 3).Value = Math.Round(item.TotalDowntimeSeconds / 3600m, 2);
                worksheet.Cell(row, 4).Value = item.TotalCycles;
                worksheet.Cell(row, 5).Value = Math.Round(item.AvgEfficiency, 2);
                row++;
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}