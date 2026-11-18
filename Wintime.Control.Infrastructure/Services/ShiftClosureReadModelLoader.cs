using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

/// <summary>Единая загрузка актуального черновика и неизменяемого снимка рапорта.</summary>
public sealed class ShiftClosureReadModelLoader
{
    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;

    public ShiftClosureReadModelLoader(
        ControlDbContext context,
        IFactoryClock factoryClock,
        IEffectiveStatusHistoryService effectiveStatusHistory)
    {
        _context = context;
        _factoryClock = factoryClock;
        _effectiveStatusHistory = effectiveStatusHistory;
    }

    public async Task<ShiftClosureDto> LoadAsync(
        Guid itemId, StaffingCaller caller, DateTime nowUtc, CancellationToken ct)
    {
        var item = await ItemQuery().SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new ShiftNotFoundException("Смена не найдена");
        EnsureAccess(item, caller);
        return item.Closure?.State == ShiftClosureState.Closed
            ? ProjectClosed(item)
            : await ProjectDraftAsync(item, Utc(nowUtc), ct);
    }

    public async Task<ShiftClosureDto> LoadClosedAsync(
        Guid closureId, StaffingCaller caller, CancellationToken ct)
    {
        var item = await ItemQuery().SingleOrDefaultAsync(i => i.Closure!.Id == closureId, ct)
            ?? throw new ShiftNotFoundException("Рапорт не найден");
        EnsureAccess(item, caller);
        if (item.Closure?.State != ShiftClosureState.Closed)
            throw new ShiftNotFoundException("Закрытый рапорт не найден");
        return ProjectClosed(item);
    }

    private IQueryable<ShiftPlanItem> ItemQuery() => _context.ShiftPlanItems
        .AsNoTracking()
        .Include(i => i.Day)
        .Include(i => i.ShiftLead)
        .Include(i => i.Closure).ThenInclude(c => c!.ClosedBy)
        .Include(i => i.Closure).ThenInclude(c => c!.Entries).ThenInclude(e => e.OutputShares)
        .AsQueryable();

    private static void EnsureAccess(ShiftPlanItem item, StaffingCaller caller)
    {
        if (string.IsNullOrEmpty(item.ShiftLeadId))
            throw new DomainException("shiftLeadRequired");
        if (caller.IsShiftLead && item.ShiftLeadId != caller.UserId)
            throw new ShiftAccessException("Старшему доступны только собственные смены");
        if (!caller.IsShiftLead && !caller.IsManagerOrAdmin)
            throw new ShiftAccessException("Недостаточно прав для чтения сменного рапорта");
    }

    private async Task<ShiftClosureDto> ProjectDraftAsync(
        ShiftPlanItem item, DateTime nowUtc, CancellationToken ct)
    {
        var shiftStart = Utc(item.StartAtUtc);
        var shiftEnd = Utc(item.EndAtUtc);
        var factEnd = nowUtc < shiftEnd ? nowUtc : shiftEnd;
        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var selectedTasks = await SelectTasksAsync(item, schedule, nowUtc, factEnd, ct);
        var taskIds = selectedTasks.Select(t => t.Entity.Id).ToList();

        var previousCycles = taskIds.Count == 0
            ? new List<ImmCycle>()
            : await _context.ImmCycles
                .AsNoTracking()
                .Include(c => c.Mold)
                .Where(c => c.TaskId != null && taskIds.Contains(c.TaskId.Value)
                    && c.IsSuccessful && c.EndTime != null && c.EndTime < shiftStart)
                .ToListAsync(ct);
        var hasAutomaticWindow = factEnd > shiftStart;
        var currentCycles = !hasAutomaticWindow || taskIds.Count == 0
            ? new List<ImmCycle>()
            : await _context.ImmCycles
                .AsNoTracking()
                .Include(c => c.Mold)
                .Where(c => c.TaskId != null && taskIds.Contains(c.TaskId.Value)
                    && c.IsSuccessful && c.EndTime != null
                    && c.EndTime >= shiftStart && c.EndTime < factEnd)
                .ToListAsync(ct);
        var assignments = await _context.ShiftImmOperatorAssignments
            .AsNoTracking()
            .Include(a => a.Operator)
            .Where(a => a.ShiftPlanItemId == item.Id)
            .ToListAsync(ct);
        var immIds = selectedTasks.Select(t => t.Entity.ImmId).Distinct().ToList();
        var rawInputs = hasAutomaticWindow && immIds.Count > 0
            ? await _effectiveStatusHistory.GatherAsync(immIds, shiftStart, factEnd, factEnd, ct)
            : new Dictionary<Guid, EffectiveStatusInputs>();

        var distributions = new Dictionary<Guid, ShiftOutputDistribution>();
        foreach (var immId in immIds)
        {
            var slices = assignments.Where(a => a.ImmId == immId)
                .Select(a => new ShiftOperatorSlice(
                    a.Id, a.OperatorId, a.Operator.FullName, a.Operator.EmployeeId, a.Slot,
                    Utc(a.AssignedAt), Utc(a.UnassignedAt ?? shiftEnd)))
                .ToList();
            var cycles = currentCycles.Where(c => c.ImmId == immId)
                .Select(c => new ShiftOutputCycle(
                    c.TaskId!.Value, Utc(c.EndTime!.Value), OutputQuantity(c)))
                .ToList();
            var windows = selectedTasks.Where(t => t.Entity.ImmId == immId)
                .ToDictionary(t => t.Entity.Id, t => t.Activity);
            distributions[immId] = ShiftOutputDistributor.Distribute(
                slices, cycles, windows, shiftStart, factEnd);
        }

        var manualByTask = item.Closure?.Entries.ToDictionary(e => e.ShiftTaskId)
            ?? new Dictionary<Guid, ShiftClosureEntry>();
        var entries = new List<ShiftClosureEntryDto>(selectedTasks.Count);
        foreach (var selected in selectedTasks)
        {
            var task = selected.Entity;
            var distribution = distributions[task.ImmId];
            var shares = distribution.Shares.Where(s => s.TaskId == task.Id)
                .OrderBy(s => s.StartUtc).ThenBy(s => s.SlotAtIntervalStart)
                .ThenBy(s => s.OperatorId, StringComparer.Ordinal).ToList();
            var metrics = ShiftClosureCycleMetrics.Calculate(
                currentCycles, task.Id, shiftStart, factEnd);
            var previousOutput = ShiftClosureCycleMetrics.PreviousOutput(
                previousCycles, task.Id, shiftStart);
            var shiftPlan = ShiftClosurePlanCalculator.Calculate(
                task, selected.Dto.PlannedStartAt, shiftStart, shiftEnd, previousOutput);
            manualByTask.TryGetValue(task.Id, out var manual);
            var raw = rawInputs.TryGetValue(task.ImmId, out var inputs)
                ? inputs.Raw
                : Array.Empty<RawSegment>();
            entries.Add(new ShiftClosureEntryDto
            {
                TaskId = task.Id,
                ImmId = task.ImmId,
                ImmName = task.Imm.Name,
                ProductName = task.Product?.Name ?? string.Empty,
                RemainingAtStart = ShiftClosurePlanCalculator.RemainingAtStart(
                    task.PlanQuantity, previousOutput),
                PlasticIngredients = Ingredients(task, MaterialType.Plastic),
                ColorantIngredients = Ingredients(task, MaterialType.Colorant),
                NormPartWeightGrams = task.PartWeightGrams,
                ActualPartWeightGrams = manual?.ActualPartWeightGrams,
                PlannedFullCycleSeconds = task.PlannedFullCycleSeconds,
                AutomaticAverageFullCycleSeconds = metrics.AverageFullCycleSeconds,
                ManualFullCycleSeconds = manual?.ManualFullCycleSeconds,
                TaskPackagingUnit = task.PackagingUnit,
                ActualPackagingUnit = manual?.ActualPackagingUnit,
                PackagingPlaces = manual?.PackagingPlaces,
                Cavities = task.Cavities,
                ShiftPlanQuantity = shiftPlan,
                PlannedMaterialWeightGrams = ShiftClosurePlanCalculator.MaterialWeightGrams(
                    shiftPlan, task.Cavities, task.PartWeightGrams,
                    task.Product?.RunnerWeightGrams ?? 0),
                AutomaticOutputQuantity = metrics.OutputQuantity,
                ManualGoodQuantity = manual?.ManualGoodQuantity,
                ManualDefectQuantity = manual?.ManualDefectQuantity,
                HasOperators = shares.Count > 0,
                IsManualFallbackRequired = ShiftOfflineFallback.IsRequired(
                    raw, selected.Activity, shiftStart, factEnd, shares.Count > 0),
                OutputShares = shares.Select(ToShareDto).ToList(),
                OperatorTotals = OperatorTotals(shares.Select(ToShareDto).ToList())
            });
        }

        var timelines = selectedTasks.GroupBy(t => t.Entity.ImmId).Select(group =>
        {
            var immTasks = group.ToList();
            var distribution = distributions[group.Key];
            return new ShiftClosureTimelineDto
            {
                ImmId = group.Key,
                ImmName = group.First().Entity.Imm.Name,
                Intervals = distribution.MergedIntervals.Select(interval =>
                {
                    var outputs = immTasks
                        .Where(t => Overlaps(t.Activity, interval.StartUtc, interval.EndUtc))
                        .Select(t => new ShiftClosureTaskOutputDto(t.Entity.Id,
                            currentCycles.Where(c => c.TaskId == t.Entity.Id
                                && c.EndTime >= interval.StartUtc && c.EndTime < interval.EndUtc)
                                .Sum(OutputQuantity)))
                        .ToList();
                    return new ShiftClosureTimelineIntervalDto
                    {
                        StartAtUtc = interval.StartUtc,
                        EndAtUtc = interval.EndUtc,
                        Operators = interval.Operators.Select(o => new ShiftClosureTimelineOperatorDto
                        {
                            OperatorId = o.OperatorId,
                            FullName = o.FullName,
                            EmployeeId = o.EmployeeId,
                            Slot = o.Slot
                        }).ToList(),
                        AutomaticOutputQuantity = outputs.Sum(o => o.Quantity),
                        HasOrphanOutput = distribution.Orphans.Any(o =>
                            o.StartUtc == interval.StartUtc && o.EndUtc == interval.EndUtc && o.Quantity > 0),
                        TaskOutputs = outputs
                    };
                }).ToList()
            };
        }).OrderBy(t => t.ImmName).ThenBy(t => t.ImmId).ToList();

        return Header(item, item.Closure, factEnd) with
        {
            Entries = entries,
            Timelines = timelines
        };
    }

    private async Task<List<SelectedTask>> SelectTasksAsync(
        ShiftPlanItem item, FactorySchedule schedule, DateTime nowUtc, DateTime factEnd, CancellationToken ct)
    {
        var shiftStart = Utc(item.StartAtUtc);
        var shiftEnd = Utc(item.EndAtUtc);
        var plannedFrom = DateOnly.FromDateTime(shiftStart.AddDays(-1));
        var plannedTo = DateOnly.FromDateTime(shiftEnd.AddDays(1));
        var taskQuery = TaskQuery();
        var plannedCandidates = await taskQuery
            .Where(t => t.PlannedDate >= plannedFrom && t.PlannedDate <= plannedTo)
            .ToListAsync(ct);
        var activityCandidates = await taskQuery
            .Where(ShiftTaskWindow.ActivityOverlaps(shiftStart, shiftEnd))
            .ToListAsync(ct);
        return plannedCandidates.Concat(activityCandidates)
            .GroupBy(t => t.Id).Select(g => g.First())
            .Select(t => new SelectedTask(t, t.ToDto(schedule), null))
            .Where(t => ShiftTaskWindow.Belongs(
                t.Entity, t.Dto.PlannedStartAt, shiftStart, shiftEnd, nowUtc))
            .OrderBy(t => t.Dto.PlannedStartAt).ThenBy(t => t.Entity.Id)
            .Select(t => t with { Activity = TaskActivityWindow.Activity(t.Entity, factEnd) })
            .ToList();
    }

    private IQueryable<ShiftTask> TaskQuery() => _context.ShiftTasks
        .AsNoTracking()
        .Include(t => t.Imm)
        .Include(t => t.Mold)
        .Include(t => t.Product).ThenInclude(p => p!.ProductType)
        .Include(t => t.Personnel)
        .Include(t => t.RecipeIngredients).ThenInclude(i => i.Material)
        .Include(t => t.RecipeIngredients).ThenInclude(i => i.OriginalMaterial)
        .Include(t => t.Order)
        .AsQueryable();

    private static ShiftClosureDto ProjectClosed(ShiftPlanItem item)
    {
        var closure = item.Closure!;
        var shiftEnd = Utc(item.EndAtUtc);
        var closedAt = closure.ClosedAt.HasValue ? Utc(closure.ClosedAt.Value) : shiftEnd;
        var factEnd = closedAt < shiftEnd ? closedAt : shiftEnd;
        var entries = closure.Entries.OrderBy(e => e.ImmNameSnapshot).ThenBy(e => e.Id)
            .Select(entry =>
            {
                var shares = entry.OutputShares.OrderBy(s => s.IntervalStartUtc)
                    .ThenBy(s => s.SlotAtIntervalStart).ThenBy(s => s.OperatorId, StringComparer.Ordinal)
                    .Select(ToShareDto).ToList();
                return new ShiftClosureEntryDto
                {
                    TaskId = entry.ShiftTaskId,
                    ImmId = entry.ImmId,
                    ImmName = entry.ImmNameSnapshot,
                    ProductName = entry.ProductNameSnapshot,
                    RemainingAtStart = entry.RemainingAtStart,
                    PlasticIngredients = entry.PlasticIngredientsSnapshot,
                    ColorantIngredients = entry.ColorantIngredientsSnapshot,
                    NormPartWeightGrams = entry.NormPartWeightGrams,
                    ActualPartWeightGrams = entry.ActualPartWeightGrams,
                    PlannedFullCycleSeconds = entry.PlannedFullCycleSeconds,
                    AutomaticAverageFullCycleSeconds = entry.AutomaticAverageFullCycleSeconds,
                    ManualFullCycleSeconds = entry.ManualFullCycleSeconds,
                    TaskPackagingUnit = entry.TaskPackagingUnit,
                    ActualPackagingUnit = entry.ActualPackagingUnit,
                    PackagingPlaces = entry.PackagingPlaces,
                    Cavities = entry.Cavities,
                    ShiftPlanQuantity = entry.ShiftPlanQuantity,
                    PlannedMaterialWeightGrams = entry.PlannedMaterialWeightGrams,
                    AutomaticOutputQuantity = entry.AutomaticOutputQuantity,
                    ManualGoodQuantity = entry.ManualGoodQuantity,
                    ManualDefectQuantity = entry.ManualDefectQuantity,
                    HasOperators = shares.Count > 0,
                    IsManualFallbackRequired = entry.IsManualFallbackRequired,
                    OutputShares = shares,
                    OperatorTotals = OperatorTotals(shares)
                };
            }).ToList();
        var timelines = closure.Entries.GroupBy(e => e.ImmId).Select(group =>
        {
            var immEntries = group.ToList();
            var intervalGroups = immEntries.SelectMany(e => e.OutputShares)
                .GroupBy(s => new { s.IntervalStartUtc, s.IntervalEndUtc })
                .OrderBy(g => g.Key.IntervalStartUtc).ToList();
            return new ShiftClosureTimelineDto
            {
                ImmId = group.Key,
                ImmName = group.First().ImmNameSnapshot,
                Intervals = intervalGroups.Select(interval =>
                {
                    var operators = interval.Select(s => new ShiftClosureTimelineOperatorDto
                    {
                        OperatorId = s.OperatorId,
                        FullName = s.OperatorFullNameSnapshot,
                        EmployeeId = s.OperatorEmployeeIdSnapshot,
                        Slot = s.SlotAtIntervalStart
                    }).GroupBy(o => o.OperatorId).Select(g => g.First()).ToList();
                    var outputs = immEntries.Select(e => new ShiftClosureTaskOutputDto(e.ShiftTaskId,
                        e.OutputShares.Where(s => s.IntervalStartUtc == interval.Key.IntervalStartUtc
                            && s.IntervalEndUtc == interval.Key.IntervalEndUtc).Sum(s => s.Quantity)))
                        .ToList();
                    return new ShiftClosureTimelineIntervalDto
                    {
                        StartAtUtc = Utc(interval.Key.IntervalStartUtc),
                        EndAtUtc = Utc(interval.Key.IntervalEndUtc),
                        Operators = operators,
                        AutomaticOutputQuantity = outputs.Sum(o => o.Quantity),
                        HasOrphanOutput = false,
                        TaskOutputs = outputs
                    };
                }).ToList()
            };
        }).OrderBy(t => t.ImmName).ThenBy(t => t.ImmId).ToList();
        return Header(item, closure, factEnd) with { Entries = entries, Timelines = timelines };
    }

    private static ShiftClosureDto Header(
        ShiftPlanItem item, ShiftClosure? closure, DateTime factEnd) => new()
    {
        Id = closure?.Id,
        ShiftPlanItemId = item.Id,
        State = closure?.State ?? ShiftClosureState.Draft,
        ShiftDate = item.Day.ShiftDate,
        StartAtUtc = Utc(item.StartAtUtc),
        EndAtUtc = Utc(item.EndAtUtc),
        FactEndUtc = Utc(factEnd),
        TimeZoneId = item.TimeZoneId,
        ShiftLeadId = item.ShiftLeadId!,
        ShiftLeadName = item.ShiftLead?.FullName ?? string.Empty,
        CreatedById = closure?.CreatedById ?? string.Empty,
        CreatedAt = closure == null ? null : Utc(closure.CreatedAt),
        ClosedById = closure?.ClosedById,
        ClosedByName = closure?.ClosedBy?.FullName,
        ClosedAt = closure?.ClosedAt == null ? null : Utc(closure.ClosedAt.Value),
        ClosedEarly = closure?.State == ShiftClosureState.Closed
            && closure.ClosedAt.HasValue && closure.ClosedAt.Value < item.EndAtUtc
    };

    private static ShiftClosureOutputShareDto ToShareDto(ShiftOutputShareModel share) => new()
    {
        IntervalStartUtc = share.StartUtc,
        IntervalEndUtc = share.EndUtc,
        OperatorId = share.OperatorId,
        OperatorFullName = share.FullName,
        OperatorEmployeeId = share.EmployeeId,
        SlotAtIntervalStart = share.SlotAtIntervalStart,
        Quantity = share.Quantity
    };

    private static ShiftClosureOutputShareDto ToShareDto(ShiftClosureOutputShare share) => new()
    {
        IntervalStartUtc = Utc(share.IntervalStartUtc),
        IntervalEndUtc = Utc(share.IntervalEndUtc),
        OperatorId = share.OperatorId,
        OperatorFullName = share.OperatorFullNameSnapshot,
        OperatorEmployeeId = share.OperatorEmployeeIdSnapshot,
        SlotAtIntervalStart = share.SlotAtIntervalStart,
        Quantity = share.Quantity
    };

    private static List<ShiftClosureOperatorTotalDto> OperatorTotals(
        IEnumerable<ShiftClosureOutputShareDto> shares) => shares
        .GroupBy(s => s.OperatorId)
        .Select(g => new ShiftClosureOperatorTotalDto
        {
            OperatorId = g.Key,
            OperatorFullName = g.First().OperatorFullName,
            OperatorEmployeeId = g.First().OperatorEmployeeId,
            Quantity = g.Sum(s => s.Quantity)
        }).OrderBy(t => t.OperatorFullName).ThenBy(t => t.OperatorId, StringComparer.Ordinal).ToList();

    private static string Ingredients(ShiftTask task, MaterialType type) => string.Join("; ",
        task.RecipeIngredients.Where(i => i.Material?.Type == type).OrderBy(i => i.SortOrder)
            .Select(i => $"{i.Material!.Name} ({i.Percent.ToString("0.##", CultureInfo.InvariantCulture)}%)"));

    private static int OutputQuantity(ImmCycle cycle) =>
        cycle.Cavities > 0 ? cycle.Cavities : cycle.Mold?.Cavities ?? 0;

    private static bool Overlaps(Interval? window, DateTime startUtc, DateTime endUtc) =>
        window != null && window.Start < endUtc && window.End > startUtc;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record SelectedTask(ShiftTask Entity, TaskDto Dto, Interval? Activity);
}
