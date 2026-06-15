using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using ClosedXML.Excel;

namespace Wintime.Control.Infrastructure.Reports;

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
    public async Task<DailyReportDto> GetDailyReportAsync(DateTime date, Guid? immId = null, Guid? shiftId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating daily report for {Date}, IMM: {ImmId}, Shift: {ShiftId}", date, immId, shiftId);

        // Границы отчётного периода: по умолчанию — сутки
        var periodStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var periodEnd = periodStart.AddDays(1);

        // Если указана смена — сужаем диапазон по её расписанию
        int? shiftNumber = null;
        string? shiftStartTime = null;
        string? shiftEndTime = null;
        if (shiftId.HasValue)
        {
            var shift = await _context.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId.Value, ct);
            if (shift != null)
            {
                periodStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc).AddMinutes(shift.StartMinutes);
                periodEnd = periodStart.AddMinutes(shift.DurationMinutes);

                var allShiftIds = await _context.Shifts.OrderBy(s => s.StartMinutes).Select(s => s.Id).ToListAsync(ct);
                shiftNumber = allShiftIds.IndexOf(shift.Id) + 1;
                shiftStartTime = $"{shift.StartMinutes / 60 % 24:D2}:{shift.StartMinutes % 60:D2}";
                var endMinutes = shift.StartMinutes + shift.DurationMinutes;
                shiftEndTime = $"{endMinutes / 60 % 24:D2}:{endMinutes % 60:D2}";
            }
        }

        var report = new DailyReportDto
        {
            Date = date.Date,
            ShiftNumber = shiftNumber,
            ShiftStartTime = shiftStartTime,
            ShiftEndTime = shiftEndTime,
            ImmData = new List<DailyReportImmItemDto>()
        };

        var immQuery = _context.Imms
            .Include(i => i.Template)
            .Where(i => i.IsActive)
            .AsQueryable();

        if (immId.HasValue)
            immQuery = immQuery.Where(i => i.Id == immId.Value);

        var imms = await immQuery.ToListAsync(ct);

        foreach (var imm in imms)
        {
            var item = await GenerateDailyImmItemAsync(imm, periodStart, periodEnd, ct);
            if (item != null)
                report.ImmData.Add(item);
        }

        return report;
    }

    /// <summary>
    /// Генерация данных по одному ТПА за период
    /// </summary>
    private async Task<DailyReportImmItemDto?> GenerateDailyImmItemAsync(Imm imm, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        // История статусов ТПА за период (перекрывающиеся записи)
        var statusHistory = await _context.ImmStatusHistory
            .Where(h => h.ImmId == imm.Id
                && h.ChangedAt < periodEnd
                && (h.EndedAt == null || h.EndedAt > periodStart))
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);

        // Успешные циклы за период
        var cycles = await _context.ImmCycles
            .Where(c => c.ImmId == imm.Id && c.IsSuccessful
                && c.StartTime >= periodStart && c.StartTime < periodEnd)
            .ToListAsync(ct);

        // Активное задание за период
        var task = await _context.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(t => t.ImmId == imm.Id
                && t.StartedAt >= periodStart && t.StartedAt < periodEnd, ct);

        // События за период (для детализации простоев)
        var events = await _context.Events
            .Where(e => e.ImmId == imm.Id
                && e.StartTime >= periodStart && e.StartTime < periodEnd)
            .Include(e => e.Reason)
            .ToListAsync(ct);

        // Расчёт времени по пяти состояниям и построение Timeline
        var workTimeSeconds = 0;
        var setupSeconds = 0;
        var downtimeSeconds = 0;
        var offlineSeconds = 0;
        var timeline = new List<TimelineItemDto>();

        foreach (var entry in statusHistory)
        {
            var start = entry.ChangedAt < periodStart ? periodStart : entry.ChangedAt;
            var end   = (entry.EndedAt == null || entry.EndedAt > periodEnd) ? periodEnd : entry.EndedAt.Value;
            if (end <= start) continue;

            var duration = (int)(end - start).TotalSeconds;
            var type = MapStatusToType(entry.Status);

            switch (type)
            {
                case "work":    workTimeSeconds += duration; break;
                case "setup":   setupSeconds    += duration; break;
                case "alarm":
                case "idle":    downtimeSeconds += duration; break;
                case "offline": offlineSeconds  += duration; break;
            }

            timeline.Add(new TimelineItemDto { Start = start, End = end, Type = type });
        }

        // Подсчёт циклов и среднего времени цикла
        var totalCycles = cycles.Count;
        var avgCycleTime = cycles.Count > 0
            ? (decimal)cycles.Average(c => c.DurationSeconds)
            : 0m;

        // Выработка считается по снапшоту гнёздности каждого цикла (ImmCycle.Cavities),
        // а не по текущему Mold.Cavities — гнёзда могли заглушаться при ремонте.
        // Fallback для старых записей (= 0) — текущее значение Mold.Cavities.
        var moldCavitiesFallback = task?.Mold?.Cavities ?? 0;
        var actualQuantity = cycles.Sum(c => c.Cavities > 0 ? c.Cavities : moldCavitiesFallback);

        // Детализация простоев по причинам
        var downtimeDetails = new List<DowntimeDetailDto>();
        foreach (var evt in events.Where(e => e.EventType == EventType.Downtime))
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

        // Расчёт расхода сырья
        decimal rawMaterialKg = 0;
        string? moldName = null;
        if (task?.Mold != null)
        {
            moldName = task.Mold.Name;
            // Вес деталей — по фактической выработке (снапшот гнёздности),
            // вес литников — по одному на каждый цикл.
            var partsGrams  = task.Mold.PartWeightGrams * actualQuantity;
            var runnerGrams = task.Mold.RunnerWeightGrams * totalCycles;
            rawMaterialKg = (partsGrams + runnerGrams) / 1000m;
        }

        // Эффективность: полезная работа / (работа + наладка + простои)
        var productiveBase = workTimeSeconds + setupSeconds + downtimeSeconds;
        var efficiency = productiveBase > 0
            ? (decimal)workTimeSeconds / productiveBase * 100
            : 0;

        return new DailyReportImmItemDto
        {
            ImmId = imm.Id,
            ImmName = imm.Name,
            MoldName = moldName,
            PlanQuantity = task?.PlanQuantity ?? 0,
            ActualQuantity = actualQuantity,
            CycleCount = totalCycles,
            WorkTimeSeconds = workTimeSeconds,
            SetupSeconds = setupSeconds,
            DowntimeSeconds = downtimeSeconds,
            OfflineSeconds = offlineSeconds,
            AvgCycleTime = avgCycleTime,
            Efficiency = efficiency,
            RawMaterialKg = rawMaterialKg,
            DowntimeDetails = downtimeDetails,
            Timeline = timeline
        };
    }

    private static string MapStatusToType(string status) => ImmMode.Normalize(status) switch
    {
        ImmMode.Auto   => "work",
        ImmMode.Manual => "setup",
        ImmMode.Alarm  => "alarm",
        ImmMode.Idle   => "idle",
        _              => "offline"
    };

    /// <summary>
    /// Отчёт "Производительность оборудования"
    /// </summary>
    public async Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo, List<Guid>? immIds = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating equipment report from {From} to {To}", dateFrom, dateTo);

        var dateFromUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc).AddDays(1);

        var report = new EquipmentReportDto
        {
            DateFrom = dateFromUtc,
            DateTo = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc),
            ImmData = [],
            DailyBreakdown = []
        };

        var immQuery = _context.Imms
            .Include(i => i.Template)
            .Where(i => i.IsActive)
            .AsQueryable();

        if (immIds != null && immIds.Count > 0)
            immQuery = immQuery.Where(i => immIds.Contains(i.Id));

        var imms = await immQuery.ToListAsync(ct);

        // Словарь для накопления поднёвных данных по всем ТПА: ключ — дата (UTC, начало дня)
        var dailyAccum = new Dictionary<DateTime, (int Work, int Setup, int Downtime)>();
        for (var d = dateFromUtc; d < periodEnd; d = d.AddDays(1))
            dailyAccum[d] = (0, 0, 0);

        foreach (var imm in imms)
        {
            var statusHistory = await _context.ImmStatusHistory
                .Where(h => h.ImmId == imm.Id
                    && h.ChangedAt < periodEnd
                    && (h.EndedAt == null || h.EndedAt > dateFromUtc))
                .OrderBy(h => h.ChangedAt)
                .ToListAsync(ct);

            var cycles = await _context.ImmCycles
                .Where(c => c.ImmId == imm.Id && c.IsSuccessful
                    && c.StartTime >= dateFromUtc && c.StartTime < periodEnd)
                .ToListAsync(ct);

            var workTimeSeconds = 0;
            var setupSeconds = 0;
            var downtimeSeconds = 0;
            var offlineSeconds = 0;

            foreach (var entry in statusHistory)
            {
                var segStart = entry.ChangedAt < dateFromUtc ? dateFromUtc : entry.ChangedAt;
                var segEnd   = (entry.EndedAt == null || entry.EndedAt > periodEnd) ? periodEnd : entry.EndedAt.Value;
                if (segEnd <= segStart) continue;

                var statusType = MapStatusToType(entry.Status);

                // Разбиваем сегмент по границам суток: каждый срез идёт и в итоги ТПА, и в дневной агрегат
                var cursor = segStart;
                while (cursor < segEnd)
                {
                    var dayStart = DateTime.SpecifyKind(cursor.Date, DateTimeKind.Utc);
                    var dayEnd   = dayStart.AddDays(1);
                    var sliceEnd = dayEnd < segEnd ? dayEnd : segEnd;
                    var secs     = (int)(sliceEnd - cursor).TotalSeconds;

                    switch (statusType)
                    {
                        case "work":    workTimeSeconds += secs; break;
                        case "setup":   setupSeconds    += secs; break;
                        case "alarm":
                        case "idle":    downtimeSeconds += secs; break;
                        case "offline": offlineSeconds  += secs; break;
                    }

                    if (dailyAccum.TryGetValue(dayStart, out var acc))
                    {
                        dailyAccum[dayStart] = statusType switch
                        {
                            "work"            => (acc.Work + secs, acc.Setup, acc.Downtime),
                            "setup"           => (acc.Work, acc.Setup + secs, acc.Downtime),
                            "alarm" or "idle" => (acc.Work, acc.Setup, acc.Downtime + secs),
                            _                 => acc
                        };
                    }

                    cursor = sliceEnd;
                }
            }

            var totalCycles = cycles.Count;
            var avgCycleSeconds = totalCycles > 0
                ? (decimal)cycles.Average(c => c.DurationSeconds)
                : 0m;

            var productiveBase = workTimeSeconds + setupSeconds + downtimeSeconds;
            var efficiency = productiveBase > 0
                ? (decimal)workTimeSeconds / productiveBase * 100
                : 0;

            report.ImmData.Add(new EquipmentReportImmItemDto
            {
                ImmId = imm.Id,
                ImmName = imm.Name,
                TotalWorkSeconds = workTimeSeconds,
                TotalSetupSeconds = setupSeconds,
                TotalDowntimeSeconds = downtimeSeconds,
                TotalOfflineSeconds = offlineSeconds,
                TotalCycles = totalCycles,
                AvgCycleSeconds = avgCycleSeconds,
                AvgEfficiency = efficiency
            });
        }

        report.DailyBreakdown = dailyAccum
            .OrderBy(kv => kv.Key)
            .Select(kv => new EquipmentReportDailyItemDto
            {
                Date = kv.Key,
                TotalWorkSeconds = kv.Value.Work,
                TotalSetupSeconds = kv.Value.Setup,
                TotalDowntimeSeconds = kv.Value.Downtime
            })
            .ToList();

        return report;
    }

    /// <summary>
    /// Отчёт "Активы цеха"
    /// </summary>
    public async Task<AssetsReportDto> GetAssetsReportAsync(DateTime dateFrom, DateTime dateTo, string reportType, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating assets report. Type: {Type}, From: {From}, To: {To}", reportType, dateFrom, dateTo);

        var dateFromUtc = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc).AddDays(1);

        var report = new AssetsReportDto
        {
            DateFrom = dateFromUtc,
            DateTo = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc),
            ReportType = reportType
        };

        if (reportType.Equals("Molds", StringComparison.OrdinalIgnoreCase))
        {
            var molds = await _context.Molds
                .Where(m => m.IsActive)
                .ToListAsync(ct);

            var moldIds = molds.Select(m => m.Id).ToList();

            // Смыкания и наработка за выбранный период
            var periodCycleStats = await _context.ImmCycles
                .Where(c => c.MoldId != null
                         && moldIds.Contains(c.MoldId.Value)
                         && c.IsSuccessful
                         && c.StartTime >= dateFromUtc
                         && c.StartTime < periodEnd)
                .GroupBy(c => c.MoldId!.Value)
                .Select(g => new
                {
                    MoldId = g.Key,
                    TotalCycles = g.Count(),
                    TotalDurationSeconds = g.Sum(c => c.DurationSeconds)
                })
                .ToListAsync(ct);

            // Суммарные смыкания за всё время — для расчёта остатка ресурса
            var allTimeCycleStats = await _context.ImmCycles
                .Where(c => c.MoldId != null && moldIds.Contains(c.MoldId.Value) && c.IsSuccessful)
                .GroupBy(c => c.MoldId!.Value)
                .Select(g => new { MoldId = g.Key, TotalCycles = g.Count() })
                .ToListAsync(ct);

            var periodLookup = periodCycleStats.ToDictionary(x => x.MoldId);
            var allTimeLookup = allTimeCycleStats.ToDictionary(x => x.MoldId);

            report.MoldData = [.. molds.Select(mold =>
            {
                periodLookup.TryGetValue(mold.Id, out var period);
                allTimeLookup.TryGetValue(mold.Id, out var allTime);
                var allTimeCycles = allTime?.TotalCycles ?? 0;
                return new AssetsMoldItemDto
                {
                    MoldId = mold.Id,
                    MoldName = mold.Name,
                    TotalCycles = period?.TotalCycles ?? 0,
                    WorkHours = (decimal)(period?.TotalDurationSeconds ?? 0) / 3600,
                    MaxResourceCycles = mold.MaxResourceCycles,
                    To1Cycles = mold.To1Cycles,
                    To2Cycles = mold.To2Cycles,
                    AllTimeTotalCycles = allTimeCycles,
                    RemainingResource = mold.MaxResourceCycles - allTimeCycles
                };
            })];
        }
        else if (reportType.Equals("Personnel", StringComparison.OrdinalIgnoreCase))
        {
            var personnel = await _context.Users
                .Where(u => u.IsActive && u.Role == UserRole.Adjuster)
                .ToListAsync(ct);

            var shifts = await _context.Shifts.ToListAsync(ct);

            report.PersonnelData = [];

            foreach (var person in personnel)
            {
                var tasks = await _context.ShiftTasks
                    .Where(t => t.PersonnelId == person.Id && t.StartedAt >= dateFromUtc && t.StartedAt < periodEnd && t.Status >= Wintime.Control.Core.Enums.TaskStatus.Completed)
                    .ToListAsync(ct);

                var completedTasks = tasks.Count;
                var totalWorkSeconds = ShiftWorkCalculator.CalculateWorkSeconds(tasks.Select(t => t.StartedAt), shifts);

                var setupEvents = await _context.Events
                    .Where(e => e.PersonnelId == person.Id && e.EventType == EventType.Setup && e.StartTime >= dateFromUtc && e.StartTime < periodEnd)
                    .ToListAsync(ct);

                var avgSetupTime = setupEvents.Count > 0
                    ? (decimal)setupEvents.Average(e => e.DurationSeconds)
                    : 0;

                var totalSetupSeconds = setupEvents.Sum(e => e.DurationSeconds);
                var workedShifts = ShiftWorkCalculator.CountWorkedShifts(tasks.Select(t => t.StartedAt), shifts);

                report.PersonnelData.Add(new AssetsPersonnelItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = completedTasks,
                    TotalWorkSeconds = totalWorkSeconds,
                    AvgSetupTime = avgSetupTime,
                    TotalSetupSeconds = totalSetupSeconds,
                    WorkedShifts = workedShifts
                });
            }
        }

        else if (reportType.Equals("MoldsByImm", StringComparison.OrdinalIgnoreCase))
        {
            var molds = await _context.Molds
                .Where(m => m.IsActive)
                .ToListAsync(ct);

            var imms = await _context.Imms
                .Where(i => i.IsActive)
                .ToListAsync(ct);

            var immNameLookup = imms.ToDictionary(i => i.Id, i => i.Name);

            var moldIds = molds.Select(m => m.Id).ToList();

            // Смыкания за период, сгруппированные по (MoldId, ImmId)
            var periodCycleStats = await _context.ImmCycles
                .Where(c => c.MoldId != null
                         && moldIds.Contains(c.MoldId.Value)
                         && c.IsSuccessful
                         && c.StartTime >= dateFromUtc
                         && c.StartTime < periodEnd)
                .GroupBy(c => new { c.MoldId, c.ImmId })
                .Select(g => new
                {
                    MoldId = g.Key.MoldId!.Value,
                    ImmId = g.Key.ImmId,
                    TotalCycles = g.Count(),
                    TotalDurationSeconds = g.Sum(c => c.DurationSeconds)
                })
                .ToListAsync(ct);

            // Суммарные смыкания за всё время — для расчёта остатка ресурса
            var allTimeCycleStats = await _context.ImmCycles
                .Where(c => c.MoldId != null && moldIds.Contains(c.MoldId.Value) && c.IsSuccessful)
                .GroupBy(c => c.MoldId!.Value)
                .Select(g => new { MoldId = g.Key, TotalCycles = g.Count() })
                .ToListAsync(ct);

            var allTimeLookup = allTimeCycleStats.ToDictionary(x => x.MoldId);

            // Группируем periodCycleStats по MoldId для построения иерархии
            var periodByMold = periodCycleStats
                .GroupBy(x => x.MoldId)
                .ToDictionary(g => g.Key, g => g.ToList());

            report.MoldsByImmData = [.. molds.Select(mold =>
            {
                allTimeLookup.TryGetValue(mold.Id, out var allTime);
                var allTimeCycles = allTime?.TotalCycles ?? 0;

                periodByMold.TryGetValue(mold.Id, out var periodRows);
                periodRows ??= [];

                var totalCycles = periodRows.Sum(r => r.TotalCycles);
                var totalDurationSeconds = periodRows.Sum(r => r.TotalDurationSeconds);

                var breakdown = periodRows
                    .Select(r => new AssetsMoldImmBreakdownDto
                    {
                        ImmId = r.ImmId,
                        ImmName = immNameLookup.GetValueOrDefault(r.ImmId, r.ImmId.ToString()),
                        TotalCycles = r.TotalCycles,
                        WorkHours = (decimal)r.TotalDurationSeconds / 3600
                    })
                    .OrderBy(r => r.ImmName)
                    .ToList();

                return new AssetsMoldByImmItemDto
                {
                    MoldId = mold.Id,
                    MoldName = mold.Name,
                    TotalCycles = totalCycles,
                    WorkHours = (decimal)totalDurationSeconds / 3600,
                    MaxResourceCycles = mold.MaxResourceCycles,
                    To1Cycles = mold.To1Cycles,
                    To2Cycles = mold.To2Cycles,
                    AllTimeTotalCycles = allTimeCycles,
                    RemainingResource = mold.MaxResourceCycles - allTimeCycles,
                    ImmBreakdown = breakdown
                };
            })];
        }

        else if (reportType.Equals("PersonnelByImm", StringComparison.OrdinalIgnoreCase))
        {
            var personnel = await _context.Users
                .Where(u => u.IsActive && u.Role == UserRole.Adjuster)
                .ToListAsync(ct);

            var imms = await _context.Imms
                .Where(i => i.IsActive)
                .ToListAsync(ct);

            var immNameLookup = imms.ToDictionary(i => i.Id, i => i.Name);

            var personnelIds = personnel.Select(p => p.Id).ToList();

            // Задания за период, сгруппированные по (PersonnelId, ImmId)
            var taskStats = await _context.ShiftTasks
                .Where(t => personnelIds.Contains(t.PersonnelId!)
                         && t.StartedAt >= dateFromUtc
                         && t.StartedAt < periodEnd
                         && t.Status >= Wintime.Control.Core.Enums.TaskStatus.Completed)
                .Select(t => new
                {
                    t.PersonnelId,
                    t.ImmId,
                    t.StartedAt,
                    t.CompletedAt
                })
                .ToListAsync(ct);

            var setupEvents = await _context.Events
                .Where(e => personnelIds.Contains(e.PersonnelId!)
                         && e.EventType == EventType.Setup
                         && e.StartTime >= dateFromUtc
                         && e.StartTime < periodEnd)
                .Select(e => new { e.PersonnelId, e.ImmId, e.DurationSeconds })
                .ToListAsync(ct);

            var setupByPerson = setupEvents
                .GroupBy(e => e.PersonnelId!)
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(e => (double)e.DurationSeconds));

            var totalSetupByPerson = setupEvents
                .GroupBy(e => e.PersonnelId!)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(e => e.DurationSeconds));

            var setupByPersonAndImm = setupEvents
                .GroupBy(e => (e.PersonnelId!, e.ImmId))
                .ToDictionary(
                    g => g.Key,
                    g => g.Average(e => (double)e.DurationSeconds));

            var totalSetupByPersonAndImm = setupEvents
                .GroupBy(e => (e.PersonnelId!, e.ImmId))
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(e => e.DurationSeconds));

            var shifts = await _context.Shifts.ToListAsync(ct);

            report.PersonnelByImmData = [.. personnel.Select(person =>
            {
                var personTasks = taskStats.Where(t => t.PersonnelId == person.Id).ToList();

                var breakdown = personTasks
                    .GroupBy(t => t.ImmId)
                    .Select(g =>
                    {
                        setupByPersonAndImm.TryGetValue((person.Id, g.Key), out var avgSetupImm);
                        totalSetupByPersonAndImm.TryGetValue((person.Id, g.Key), out var totalSetupImm);
                        return new AssetsPersonnelImmBreakdownDto
                        {
                            ImmId = g.Key,
                            ImmName = immNameLookup.GetValueOrDefault(g.Key, g.Key.ToString()),
                            CompletedTasks = g.Count(),
                            TotalWorkSeconds = ShiftWorkCalculator.CalculateWorkSeconds(g.Select(t => t.StartedAt), shifts),
                            AvgSetupTime = (decimal)avgSetupImm,
                            TotalSetupSeconds = totalSetupImm
                        };
                    })
                    .OrderBy(r => r.ImmName)
                    .ToList();

                setupByPerson.TryGetValue(person.Id, out var avgSetup);
                totalSetupByPerson.TryGetValue(person.Id, out var totalSetup);

                return new AssetsPersonnelByImmItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = personTasks.Count,
                    TotalWorkSeconds = ShiftWorkCalculator.CalculateWorkSeconds(personTasks.Select(t => t.StartedAt), shifts),
                    WorkedShifts = ShiftWorkCalculator.CountWorkedShifts(personTasks.Select(t => t.StartedAt), shifts),
                    AvgSetupTime = (decimal)avgSetup,
                    TotalSetupSeconds = totalSetup,
                    ImmBreakdown = breakdown
                };
            })];
        }

        return report;
    }

    /// <summary>
    /// Экспорт в Excel (ClosedXML)
    /// </summary>
    public async Task<byte[]> ExportToExcelAsync<T>(T data, string reportType, CancellationToken ct = default) where T : class
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add($"Report_{reportType}");

        var row = 1;
        var col = 1;

        // Заголовок
        var reportTitle = reportType.ToLowerInvariant() switch
        {
            "daily" => "Картина рабочего дня",
            "equipment" => "Производительность оборудования",
            "assets" => "Активы цеха",
            _ => reportType
        };
        worksheet.Cell(row, col).Value = "Отчёт:";
        worksheet.Cell(row, col).Style.Font.Bold = true;
        worksheet.Cell(row, col).Style.Font.FontSize = 14;
        worksheet.Cell(row, col + 1).Value = reportTitle;
        worksheet.Cell(row, col + 1).Style.Font.Bold = true;
        worksheet.Cell(row, col + 1).Style.Font.FontSize = 14;
        row += 2;

        // Динамическое заполнение в зависимости от типа отчёта
        if (data is DailyReportDto dailyReport)
        {
            // Мета-информация: дата, смена, время формирования
            worksheet.Cell(row, 1).Value = "Дата:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = dailyReport.Date.ToString("dd.MM.yyyy");
            row++;

            if (dailyReport.ShiftNumber.HasValue)
            {
                worksheet.Cell(row, 1).Value = "Смена:";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 2).Value = $"Смена {dailyReport.ShiftNumber} ({dailyReport.ShiftStartTime}–{dailyReport.ShiftEndTime})";
                row++;
            }

            worksheet.Cell(row, 1).Value = "Сформирован:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            row += 2;

            // Заголовки таблицы
            var headers = new[] { "ТПА", "Пресс-форма", "План", "Факт", "Циклы", "Работа (ч)", "Наладка (ч)", "Простой (ч)", "Эффективность %", "Сырьё (кг)" };
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cell(row, i + 1).Value = headers[i];
                worksheet.Cell(row, i + 1).Style.Font.Bold = true;
            }
            row++;

            foreach (var item in dailyReport.ImmData)
            {
                worksheet.Cell(row, 1).Value = item.ImmName;
                worksheet.Cell(row, 2).Value = item.MoldName;
                worksheet.Cell(row, 3).Value = item.PlanQuantity;
                worksheet.Cell(row, 4).Value = item.ActualQuantity;
                worksheet.Cell(row, 5).Value = item.CycleCount;
                worksheet.Cell(row, 6).Value = Math.Round(item.WorkTimeSeconds / 3600m, 2);
                worksheet.Cell(row, 7).Value = Math.Round(item.SetupSeconds / 3600m, 2);
                worksheet.Cell(row, 8).Value = Math.Round(item.DowntimeSeconds / 3600m, 2);
                worksheet.Cell(row, 9).Value = Math.Round(item.Efficiency, 2);
                worksheet.Cell(row, 10).Value = Math.Round(item.RawMaterialKg, 2);
                row++;
            }
        }
        else if (data is EquipmentReportDto equipmentReport)
        {
            worksheet.Cell(row, 1).Value = "Период:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = $"{equipmentReport.DateFrom:dd.MM.yyyy} – {equipmentReport.DateTo:dd.MM.yyyy}";
            row++;

            worksheet.Cell(row, 1).Value = "Сформирован:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            row += 2;

            var headers = new[] { "ТПА", "Работа (ч)", "Наладка (ч)", "Простой (ч)", "Офлайн (ч)", "Циклы", "Ср. цикл (с)", "Эффективность %" };
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
                worksheet.Cell(row, 3).Value = Math.Round(item.TotalSetupSeconds / 3600m, 2);
                worksheet.Cell(row, 4).Value = Math.Round(item.TotalDowntimeSeconds / 3600m, 2);
                worksheet.Cell(row, 5).Value = Math.Round(item.TotalOfflineSeconds / 3600m, 2);
                worksheet.Cell(row, 6).Value = item.TotalCycles;
                worksheet.Cell(row, 7).Value = Math.Round(item.AvgCycleSeconds, 1);
                worksheet.Cell(row, 8).Value = Math.Round(item.AvgEfficiency, 2);
                row++;
            }

            // Строка "Итого"
            var dataRows = equipmentReport.ImmData;
            if (dataRows.Count > 0)
            {
                worksheet.Cell(row, 1).Value = "Итого:";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 2).Value = Math.Round(dataRows.Sum(i => i.TotalWorkSeconds) / 3600m, 2);
                worksheet.Cell(row, 3).Value = Math.Round(dataRows.Sum(i => i.TotalSetupSeconds) / 3600m, 2);
                worksheet.Cell(row, 4).Value = Math.Round(dataRows.Sum(i => i.TotalDowntimeSeconds) / 3600m, 2);
                worksheet.Cell(row, 5).Value = Math.Round(dataRows.Sum(i => i.TotalOfflineSeconds) / 3600m, 2);
                worksheet.Cell(row, 6).Value = "—";
                var activeItems = dataRows.Where(i => i.AvgCycleSeconds > 0).ToList();
                worksheet.Cell(row, 7).Value = activeItems.Count > 0
                    ? Math.Round(activeItems.Average(i => i.AvgCycleSeconds), 1).ToString()
                    : "—";
                var effItems = dataRows.Where(i => i.AvgEfficiency > 0).ToList();
                worksheet.Cell(row, 8).Value = effItems.Count > 0
                    ? Math.Round(effItems.Average(i => i.AvgEfficiency), 2)
                    : 0;
                for (var c = 1; c <= 8; c++)
                    worksheet.Cell(row, c).Style.Font.Bold = true;
            }
        }
        else if (data is AssetsReportDto assetsReport)
        {
            worksheet.Cell(row, 1).Value = "Период:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = $"{assetsReport.DateFrom:dd.MM.yyyy} – {assetsReport.DateTo:dd.MM.yyyy}";
            row++;

            worksheet.Cell(row, 1).Value = "Сформирован:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            row += 2;

            if (assetsReport.MoldData is { Count: > 0 })
            {
                worksheet.Cell(row, 1).Value = "Пресс-формы";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 1).Style.Font.FontSize = 12;
                row++;

                var moldHeaders = new[] { "Пресс-форма", "Циклы", "Работа (ч)", "Остаток ресурса" };
                for (int i = 0; i < moldHeaders.Length; i++)
                {
                    worksheet.Cell(row, i + 1).Value = moldHeaders[i];
                    worksheet.Cell(row, i + 1).Style.Font.Bold = true;
                }
                row++;

                foreach (var item in assetsReport.MoldData)
                {
                    worksheet.Cell(row, 1).Value = item.MoldName;
                    worksheet.Cell(row, 2).Value = item.TotalCycles;
                    worksheet.Cell(row, 3).Value = Math.Round(item.WorkHours, 2);
                    worksheet.Cell(row, 4).Value = item.RemainingResource;
                    row++;
                }
                row++;
            }

            if (assetsReport.PersonnelData is { Count: > 0 })
            {
                worksheet.Cell(row, 1).Value = "Персонал";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 1).Style.Font.FontSize = 12;
                row++;

                var personnelHeaders = new[] { "Сотрудник", "Выполнено задач", "Время работы (ч)", "Ср. время наладки (мин)" };
                for (int i = 0; i < personnelHeaders.Length; i++)
                {
                    worksheet.Cell(row, i + 1).Value = personnelHeaders[i];
                    worksheet.Cell(row, i + 1).Style.Font.Bold = true;
                }
                row++;

                foreach (var item in assetsReport.PersonnelData)
                {
                    worksheet.Cell(row, 1).Value = item.FullName;
                    worksheet.Cell(row, 2).Value = item.CompletedTasks;
                    worksheet.Cell(row, 3).Value = Math.Round(item.TotalWorkSeconds / 3600m, 2);
                    worksheet.Cell(row, 4).Value = Math.Round(item.AvgSetupTime / 60m, 1);
                    row++;
                }
            }

            if (assetsReport.PersonnelByImmData is { Count: > 0 })
            {
                worksheet.Cell(row, 1).Value = "Наладчики по ТПА";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 1).Style.Font.FontSize = 12;
                row++;

                var personnelByImmHeaders = new[] { "Наладчик / ТПА", "Выполнено задач", "Время работы (ч)", "Ср. время наладки (мин)" };
                for (int i = 0; i < personnelByImmHeaders.Length; i++)
                {
                    worksheet.Cell(row, i + 1).Value = personnelByImmHeaders[i];
                    worksheet.Cell(row, i + 1).Style.Font.Bold = true;
                }
                row++;

                foreach (var person in assetsReport.PersonnelByImmData)
                {
                    worksheet.Cell(row, 1).Value = person.FullName;
                    worksheet.Cell(row, 1).Style.Font.Bold = true;
                    worksheet.Cell(row, 2).Value = person.CompletedTasks;
                    worksheet.Cell(row, 3).Value = Math.Round(person.TotalWorkSeconds / 3600m, 2);
                    worksheet.Cell(row, 4).Value = Math.Round(person.AvgSetupTime / 60m, 1);
                    row++;

                    foreach (var imm in person.ImmBreakdown)
                    {
                        worksheet.Cell(row, 1).Value = $"  {imm.ImmName}";
                        worksheet.Cell(row, 2).Value = imm.CompletedTasks;
                        worksheet.Cell(row, 3).Value = Math.Round(imm.TotalWorkSeconds / 3600m, 2);
                        row++;
                    }
                }
                row++;
            }

            if (assetsReport.MoldsByImmData is { Count: > 0 })
            {
                worksheet.Cell(row, 1).Value = "Пресс-формы по ТПА";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 1).Style.Font.FontSize = 12;
                row++;

                var moldsByImmHeaders = new[] { "Пресс-форма / ТПА", "Смыканий", "Наработка (ч)", "Остаток ресурса" };
                for (int i = 0; i < moldsByImmHeaders.Length; i++)
                {
                    worksheet.Cell(row, i + 1).Value = moldsByImmHeaders[i];
                    worksheet.Cell(row, i + 1).Style.Font.Bold = true;
                }
                row++;

                foreach (var mold in assetsReport.MoldsByImmData)
                {
                    worksheet.Cell(row, 1).Value = mold.MoldName;
                    worksheet.Cell(row, 1).Style.Font.Bold = true;
                    worksheet.Cell(row, 2).Value = mold.TotalCycles;
                    worksheet.Cell(row, 3).Value = Math.Round(mold.WorkHours, 2);
                    worksheet.Cell(row, 4).Value = mold.RemainingResource;
                    row++;

                    foreach (var imm in mold.ImmBreakdown)
                    {
                        worksheet.Cell(row, 1).Value = $"  {imm.ImmName}";
                        worksheet.Cell(row, 2).Value = imm.TotalCycles;
                        worksheet.Cell(row, 3).Value = Math.Round(imm.WorkHours, 2);
                        row++;
                    }
                }
            }
        }

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}