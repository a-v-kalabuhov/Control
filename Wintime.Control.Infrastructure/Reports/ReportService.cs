using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using ClosedXML.Excel;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly ControlDbContext _context;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;
    private readonly ILogger<ReportService> _logger;

    public ReportService(ControlDbContext context, IEffectiveStatusHistoryService effectiveStatusHistory,
        ILogger<ReportService> logger)
    {
        _context = context;
        _effectiveStatusHistory = effectiveStatusHistory;
        _logger = logger;
    }

    /// <summary>
    /// Отчёт "Картина рабочего дня"
    /// </summary>
    public async Task<DailyReportDto> GetDailyReportAsync(DateTime date, Guid? immId = null, Guid? shiftId = null, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating daily report for {Date}, IMM: {ImmId}, Shift: {ShiftId}", date, immId, shiftId);

        // Сутки/смена — в локальном времени завода (зона смен), по умолчанию — сутки
        var shifts = await _context.Shifts.OrderBy(s => s.StartMinutes).ToListAsync(ct);
        var shift = shiftId.HasValue ? shifts.FirstOrDefault(s => s.Id == shiftId.Value) : null;
        var timeZoneId = shifts.FirstOrDefault()?.TimeZoneId ?? FactoryCalendar.DefaultTimeZoneId;
        var period = DailyReportPeriod.Resolve(date, shift, timeZoneId);

        int? shiftNumber = null;
        string? shiftStartTime = null;
        string? shiftEndTime = null;
        if (shift != null)
        {
            shiftNumber = shifts.IndexOf(shift) + 1;
            shiftStartTime = $"{shift.StartMinutes / 60 % 24:D2}:{shift.StartMinutes % 60:D2}";
            var endMinutes = shift.StartMinutes + shift.DurationMinutes;
            shiftEndTime = $"{endMinutes / 60 % 24:D2}:{endMinutes % 60:D2}";
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
            var item = await GenerateDailyImmItemAsync(imm, period, ct);
            if (item != null)
                report.ImmData.Add(item);
        }

        return report;
    }

    /// <summary>
    /// Генерация данных по одному ТПА за период
    /// </summary>
    private async Task<DailyReportImmItemDto?> GenerateDailyImmItemAsync(Imm imm, DailyReportPeriod period, CancellationToken ct)
    {
        var periodStart = period.PeriodStart;
        var periodEnd = period.PeriodEnd;

        // История статусов ТПА за окно графика (оно шире периода агрегатов на поля смены)
        var statusHistory = await _context.ImmStatusHistory
            .Where(h => h.ImmId == imm.Id
                && h.ChangedAt < period.TimelineEnd
                && (h.EndedAt == null || h.EndedAt > period.TimelineStart))
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);

        // Успешные циклы за период
        var cycles = await _context.ImmCycles
            .Where(c => c.ImmId == imm.Id && c.IsSuccessful && c.EndTime != null
                && c.StartTime >= periodStart && c.StartTime < periodEnd)
            .ToListAsync(ct);

        // Задания, чьё окно активности пересекается с периодом (ARCH-03): задание может идти
        // через несколько смен и суток, поэтому отбор — не по одной точке StartedAt.
        var tasks = await _context.ShiftTasks
            .Include(t => t.Mold)
            .Where(t => t.ImmId == imm.Id)
            .Where(TaskActivityWindow.OverlapsPeriod(periodStart, periodEnd))
            .OrderBy(t => t.SetupStartedAt)
            .ToListAsync(ct);

        // ПФ циклов периода — для расхода сырья по ПФ каждого цикла
        var cycleMoldIds = cycles.Where(c => c.MoldId != null).Select(c => c.MoldId!.Value).Distinct().ToList();
        var cycleMolds = await _context.Molds
            .Where(m => cycleMoldIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

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

        // Незакрытый (текущий) статус тянется только до «сейчас», а не до конца окна —
        // иначе для сегодняшнего дня активный статус закрашивал бы будущее до конца суток.
        var now = DateTime.UtcNow;
        var timelineEnd = period.TimelineEnd < now ? period.TimelineEnd : now;
        var aggregateEnd = periodEnd < now ? periodEnd : now;

        foreach (var entry in statusHistory)
        {
            var entryEnd = entry.EndedAt ?? DateTime.MaxValue;
            var type = MapStatusToType(entry.Status);

            // Сегмент графика — в окне графика (смена ±30 мин)
            var tlStart = entry.ChangedAt < period.TimelineStart ? period.TimelineStart : entry.ChangedAt;
            var tlEnd   = entryEnd > timelineEnd ? timelineEnd : entryEnd;
            if (tlEnd > tlStart)
                timeline.Add(new TimelineItemDto { Start = tlStart, End = tlEnd, Type = type });

            // Агрегаты — только в пределах периода (смены/суток)
            var start = entry.ChangedAt < periodStart ? periodStart : entry.ChangedAt;
            var end   = entryEnd > aggregateEnd ? aggregateEnd : entryEnd;
            if (end <= start) continue;

            var duration = (int)(end - start).TotalSeconds;

            switch (type)
            {
                case "work":    workTimeSeconds += duration; break;
                case "setup":   setupSeconds    += duration; break;
                case "alarm":
                case "idle":    downtimeSeconds += duration; break;
                case "offline": offlineSeconds  += duration; break;
            }
        }

        // Подсчёт циклов и среднего времени цикла
        var totalCycles = cycles.Count;
        var avgCycleTime = cycles.Count > 0
            ? (decimal)cycles.Average(c => c.DurationSeconds)
            : 0m;

        // Выработка — по снапшоту гнёздности каждого цикла (ImmCycle.Cavities), не по текущему Mold.Cavities.
        var actualQuantity = cycles.Sum(c => c.Cavities);

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

        // Расход сырья — по ПФ каждого цикла: деталь × гнёзда цикла + один литник на цикл.
        // Циклы без ПФ (MoldId = null) сырьё не считают.
        decimal rawGrams = 0;
        foreach (var c in cycles)
            if (c.MoldId is Guid moldId && cycleMolds.TryGetValue(moldId, out var cm))
                rawGrams += cm.PartWeightGrams * c.Cavities + cm.RunnerWeightGrams;
        var rawMaterialKg = rawGrams / 1000m;

        var moldNames = tasks.Select(t => t.Mold?.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        string? moldName = moldNames.Count > 0 ? string.Join(", ", moldNames) : null;

        // Нормативный план за период (ARCH-03): сумма по заданиям; для текущих суток — до «сейчас».
        var planQuantity = tasks.Sum(t => TaskPeriodPlan.Calculate(
            TaskActivityWindow.Release(t, now), t.PlanQuantity, t.Mold?.Cavities ?? 0,
            t.PlannedFullCycleSeconds, periodStart, aggregateEnd));

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
            PlanQuantity = planQuantity,
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

    // Зона завода — зона смен (у всех смен она общая, задаётся в справочнике смен)
    private async Task<string> GetFactoryTimeZoneIdAsync(CancellationToken ct) =>
        await _context.Shifts.OrderBy(s => s.StartMinutes).Select(s => (string?)s.TimeZoneId).FirstOrDefaultAsync(ct)
            ?? FactoryCalendar.DefaultTimeZoneId;

    // Строка задания наладчика для отчётов по персоналу (ARCH-03): задание завершено в периоде
    // и/или наладка завершена в периоде (тогда — её длительность). Наладка относится к периоду
    // своего завершения целиком; наладчик — PersonnelId задания (после начала наладки не меняется).
    private sealed record PersonnelTaskRow(string PersonnelId, Guid ImmId, bool CompletedInPeriod, int? SetupSeconds);

    private readonly record struct PersonnelStats(int CompletedTasks, int CompletedSetups, int TotalSetupSeconds, decimal AvgSetupSeconds)
    {
        public static PersonnelStats Of(IEnumerable<PersonnelTaskRow> rows)
        {
            var list = rows.ToList();
            var setups = list.Where(r => r.SetupSeconds.HasValue).Select(r => r.SetupSeconds!.Value).ToList();
            var total = setups.Sum();
            return new PersonnelStats(list.Count(r => r.CompletedInPeriod), setups.Count, total,
                setups.Count > 0 ? (decimal)total / setups.Count : 0m);
        }
    }

    private async Task<List<PersonnelTaskRow>> LoadPersonnelTaskRowsAsync(
        List<string> personnelIds, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var rows = await _context.ShiftTasks
            .Where(t => t.PersonnelId != null && personnelIds.Contains(t.PersonnelId)
                && ((t.CompletedAt != null && t.CompletedAt >= fromUtc && t.CompletedAt < toUtc)
                    || (t.SetupStartedAt != null && t.StartedAt >= fromUtc && t.StartedAt < toUtc)))
            .Select(t => new { t.PersonnelId, t.ImmId, t.CompletedAt, t.SetupStartedAt, t.StartedAt })
            .ToListAsync(ct);

        return rows.Select(r => new PersonnelTaskRow(
            r.PersonnelId!,
            r.ImmId,
            r.CompletedAt != null && r.CompletedAt >= fromUtc && r.CompletedAt < toUtc,
            r.SetupStartedAt != null && r.StartedAt >= fromUtc && r.StartedAt < toUtc
                ? Math.Max(0, (int)(r.StartedAt!.Value - r.SetupStartedAt.Value).TotalSeconds)
                : null)).ToList();
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
    /// Отчёт "Производительность оборудования": по каждому ТПА — секунды эффективного состояния
    /// (+ «Нет данных») по локальным суткам завода.
    /// </summary>
    public async Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo,
        List<Guid>? immIds = null, ArchiveFilter archive = ArchiveFilter.Exclude, CancellationToken ct = default)
    {
        if (dateFrom.Date > dateTo.Date)
            throw new ArgumentException("Дата начала периода позже даты окончания");

        _logger.LogInformation("Generating equipment report from {From} to {To}, archive: {Archive}", dateFrom, dateTo, archive);

        // Сутки — локальные сутки завода (зона смен), не UTC
        var days = FactoryCalendar.Days(dateFrom, dateTo, await GetFactoryTimeZoneIdAsync(ct));
        var periodStart = days[0].StartUtc;
        var periodEnd   = days[^1].EndUtc;

        // Таймлайн строится только до «сейчас»; остаток суток Split заполнит «Нет данных».
        var nowUtc = DateTime.UtcNow;
        var windowEnd = periodEnd < nowUtc ? periodEnd : nowUtc;

        var immQuery = archive switch
        {
            ArchiveFilter.Include => _context.Imms.AsQueryable(),
            ArchiveFilter.Only    => _context.Imms.Where(i => !i.IsActive),
            _                     => _context.Imms.Where(i => i.IsActive)
        };
        if (immIds is { Count: > 0 })
            immQuery = immQuery.Where(i => immIds.Contains(i.Id));

        var imms = await immQuery.OrderBy(i => i.Name).ToListAsync(ct);
        var ids = imms.Select(i => i.Id).ToList();

        var inputs = await _effectiveStatusHistory.GatherAsync(ids, periodStart, periodEnd, windowEnd, ct);

        // Только закрытые успешные циклы. Средний цикл здесь не считаем: за период ТПА мог делать
        // разные изделия, и средний по ним ничего не значит — цикл показывается по заданию.
        var cycleCounts = await _context.ImmCycles
            .Where(c => ids.Contains(c.ImmId) && c.IsSuccessful && c.EndTime != null
                && c.StartTime >= periodStart && c.StartTime < periodEnd)
            .GroupBy(c => c.ImmId)
            .Select(g => new { ImmId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ImmId, x => x.Count, ct);

        var report = new EquipmentReportDto
        {
            DateFrom = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc),
            DateTo   = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc),
        };

        foreach (var imm in imms)
        {
            var inp = inputs[imm.Id];
            var segments = EffectiveStatusTimeline.Build(
                inp.Raw, inp.Tasks, inp.Downtimes, periodStart, windowEnd, gapAsNoData: true);
            var perDay = DailyStatusBreakdown.Split(segments, days);

            var total = DailyStatusBreakdown.Empty();
            foreach (var day in perDay)
                foreach (var (key, secs) in day)
                    total[key] += secs;

            report.ImmData.Add(new EquipmentReportImmItemDto
            {
                ImmId = imm.Id,
                ImmName = imm.Name,
                IsActive = imm.IsActive,
                Seconds = total,
                TotalCycles = cycleCounts.GetValueOrDefault(imm.Id),
                Efficiency = DailyStatusBreakdown.Efficiency(total),
                Days = days.Select((day, i) => new EquipmentReportDayDto
                {
                    Date = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc),
                    Seconds = perDay[i]
                }).ToList()
            });
        }

        return report;
    }

    private static int WriteEquipmentSheet(IXLWorksheet worksheet, int row, EquipmentReportDto report)
    {
        worksheet.Cell(row, 1).Value = "Период:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Value = $"{report.DateFrom:dd.MM.yyyy} – {report.DateTo:dd.MM.yyyy}";
        row++;

        worksheet.Cell(row, 1).Value = "Сформирован:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Value = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        row += 2;

        var keys = DailyStatusBreakdown.Keys;
        var headers = new List<string> { "ТПА" };
        headers.AddRange(keys.Select(k => $"{DailyStatusBreakdown.Labels[k]} (ч)"));
        headers.AddRange(["Циклы", "Эффективность %"]);
        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(row, i + 1).Value = headers[i];
            worksheet.Cell(row, i + 1).Style.Font.Bold = true;
        }
        row++;

        var cyclesCol = keys.Length + 2;
        foreach (var item in report.ImmData)
        {
            worksheet.Cell(row, 1).Value = item.IsActive ? item.ImmName : $"{item.ImmName} (архив)";
            for (var k = 0; k < keys.Length; k++)
                worksheet.Cell(row, k + 2).Value = Math.Round(item.Seconds.GetValueOrDefault(keys[k]) / 3600m, 2);
            worksheet.Cell(row, cyclesCol).Value = item.TotalCycles;
            if (item.Efficiency.HasValue)
                worksheet.Cell(row, cyclesCol + 1).Value = Math.Round(item.Efficiency.Value, 2);
            else
                worksheet.Cell(row, cyclesCol + 1).Value = "—";
            row++;
        }

        if (report.ImmData.Count > 0)
        {
            var totals = DailyStatusBreakdown.Keys.ToDictionary(k => k, _ => 0L);   // long: N ТПА × сутки могут превысить int
            foreach (var item in report.ImmData)
                foreach (var k in keys)
                    totals[k] += item.Seconds.GetValueOrDefault(k);

            worksheet.Cell(row, 1).Value = "Итого:";
            for (var k = 0; k < keys.Length; k++)
                worksheet.Cell(row, k + 2).Value = Math.Round(totals[keys[k]] / 3600m, 2);
            worksheet.Cell(row, cyclesCol).Value = report.ImmData.Sum(i => i.TotalCycles);
            var fleet = DailyStatusBreakdown.Efficiency(totals);   // взвешенная эффективность парка
            if (fleet.HasValue)
                worksheet.Cell(row, cyclesCol + 1).Value = Math.Round(fleet.Value, 2);
            else
                worksheet.Cell(row, cyclesCol + 1).Value = "—";
            for (var c = 1; c <= headers.Count; c++)
                worksheet.Cell(row, c).Style.Font.Bold = true;
            row++;
        }

        return row;
    }

    /// <summary>
    /// Отчёт "Активы цеха"
    /// </summary>
    public async Task<AssetsReportDto> GetAssetsReportAsync(DateTime dateFrom, DateTime dateTo, string reportType, CancellationToken ct = default)
    {
        _logger.LogInformation("Generating assets report. Type: {Type}, From: {From}, To: {To}", reportType, dateFrom, dateTo);

        // Сутки — локальные сутки завода (зона смен), не UTC
        var tz = TimeZoneInfo.FindSystemTimeZoneById(await GetFactoryTimeZoneIdAsync(ct));
        var dateFromUtc = FactoryCalendar.DayStartUtc(dateFrom, tz);
        var periodEnd = FactoryCalendar.DayStartUtc(dateTo.Date.AddDays(1), tz);

        var report = new AssetsReportDto
        {
            DateFrom = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc),
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
                         && c.EndTime != null
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

            // Суммарные смыкания за всё время — для расчёта остатка ресурса (включая
            // открытые циклы: незавершённый цикл уже изнашивает форму)
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

            var rows = await LoadPersonnelTaskRowsAsync(personnel.Select(p => p.Id).ToList(), dateFromUtc, periodEnd, ct);

            report.PersonnelData = [.. personnel.Select(person =>
            {
                var s = PersonnelStats.Of(rows.Where(r => r.PersonnelId == person.Id));
                return new AssetsPersonnelItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = s.CompletedTasks,
                    CompletedSetups = s.CompletedSetups,
                    AvgSetupTime = s.AvgSetupSeconds,
                    TotalSetupSeconds = s.TotalSetupSeconds
                };
            })];
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
                         && c.EndTime != null
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

            // Суммарные смыкания за всё время — для расчёта остатка ресурса (включая
            // открытые циклы: незавершённый цикл уже изнашивает форму)
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

            var immNameLookup = await _context.Imms.ToDictionaryAsync(i => i.Id, i => i.Name, ct);

            var rows = await LoadPersonnelTaskRowsAsync(personnel.Select(p => p.Id).ToList(), dateFromUtc, periodEnd, ct);

            report.PersonnelByImmData = [.. personnel.Select(person =>
            {
                var personRows = rows.Where(r => r.PersonnelId == person.Id).ToList();
                var total = PersonnelStats.Of(personRows);

                var breakdown = personRows
                    .GroupBy(r => r.ImmId)
                    .Select(g =>
                    {
                        var s = PersonnelStats.Of(g);
                        return new AssetsPersonnelImmBreakdownDto
                        {
                            ImmId = g.Key,
                            ImmName = immNameLookup.GetValueOrDefault(g.Key, g.Key.ToString()),
                            CompletedTasks = s.CompletedTasks,
                            CompletedSetups = s.CompletedSetups,
                            AvgSetupTime = s.AvgSetupSeconds,
                            TotalSetupSeconds = s.TotalSetupSeconds
                        };
                    })
                    .OrderBy(r => r.ImmName)
                    .ToList();

                return new AssetsPersonnelByImmItemDto
                {
                    PersonnelId = person.Id,
                    FullName = person.FullName,
                    CompletedTasks = total.CompletedTasks,
                    CompletedSetups = total.CompletedSetups,
                    AvgSetupTime = total.AvgSetupSeconds,
                    TotalSetupSeconds = total.TotalSetupSeconds,
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
            row = WriteEquipmentSheet(worksheet, row, equipmentReport);
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

                var personnelHeaders = new[] { "Сотрудник", "Выполнено задач", "Выполнено наладок", "Ср. время наладки (мин)", "Время наладки (ч)" };
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
                    worksheet.Cell(row, 3).Value = item.CompletedSetups;
                    worksheet.Cell(row, 4).Value = Math.Round(item.AvgSetupTime / 60m, 1);
                    worksheet.Cell(row, 5).Value = Math.Round(item.TotalSetupSeconds / 3600m, 2);
                    row++;
                }
            }

            if (assetsReport.PersonnelByImmData is { Count: > 0 })
            {
                worksheet.Cell(row, 1).Value = "Наладчики по ТПА";
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 1).Style.Font.FontSize = 12;
                row++;

                var personnelByImmHeaders = new[] { "Наладчик / ТПА", "Выполнено задач", "Выполнено наладок", "Ср. время наладки (мин)", "Время наладки (ч)" };
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
                    worksheet.Cell(row, 3).Value = person.CompletedSetups;
                    worksheet.Cell(row, 4).Value = Math.Round(person.AvgSetupTime / 60m, 1);
                    worksheet.Cell(row, 5).Value = Math.Round(person.TotalSetupSeconds / 3600m, 2);
                    row++;

                    foreach (var imm in person.ImmBreakdown)
                    {
                        worksheet.Cell(row, 1).Value = $"  {imm.ImmName}";
                        worksheet.Cell(row, 2).Value = imm.CompletedTasks;
                        worksheet.Cell(row, 3).Value = imm.CompletedSetups;
                        worksheet.Cell(row, 4).Value = Math.Round(imm.AvgSetupTime / 60m, 1);
                        worksheet.Cell(row, 5).Value = Math.Round(imm.TotalSetupSeconds / 3600m, 2);
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