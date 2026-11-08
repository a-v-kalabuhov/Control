using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftStaffingService : IShiftStaffingService
{
    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IImmStatusCache _statusCache;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;
    private readonly ITaskResultsService _taskResults;
    private readonly DowntimeSettings _downtime;

    public ShiftStaffingService(
        ControlDbContext context,
        IFactoryClock factoryClock,
        IImmStatusCache statusCache,
        IEffectiveStatusHistoryService effectiveStatusHistory,
        ITaskResultsService taskResults,
        IOptions<DowntimeSettings> downtime)
    {
        _context = context;
        _factoryClock = factoryClock;
        _statusCache = statusCache;
        _effectiveStatusHistory = effectiveStatusHistory;
        _taskResults = taskResults;
        _downtime = downtime.Value;
    }

    public async Task<NearestShiftDto> GetNearestAsync(string userId, CancellationToken ct)
    {
        var now = Now;
        var candidates = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Where(i => i.ShiftLeadId == userId && i.EndAtUtc > now)
            .ToListAsync(ct);
        var nearest = NearestShiftSelector.ForLead(candidates, userId, now);
        return new NearestShiftDto(nearest == null
            ? null
            : ToSummary(nearest, now, CanEditNearestForLead(nearest, userId, now)));
    }

    public async Task<IReadOnlyList<ShiftStaffingSummaryDto>> GetHistoryAsync(
        string userId, CancellationToken ct)
    {
        var now = Now;
        var items = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Where(i => i.ShiftLeadId == userId && i.EndAtUtc <= now)
            .OrderByDescending(i => i.StartAtUtc)
            .ThenByDescending(i => i.Id)
            .ToListAsync(ct);
        return items.Select(i => ToSummary(i, now, canEditOperators: false)).ToList();
    }

    public Task<ShiftStaffingItemDto> GetItemAsync(
        Guid itemId, StaffingCaller caller, CancellationToken ct) =>
        LoadAccessibleItemAsync(itemId, caller, ct);

    public async Task<ShiftStaffingTaskDto> GetTaskAsync(
        Guid itemId, Guid taskId, StaffingCaller caller, CancellationToken ct)
    {
        var item = await LoadAccessibleItemAsync(itemId, caller, ct);
        var task = await TaskQuery().SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new ShiftNotFoundException("Задание не найдено в этой смене");
        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var dto = task.ToDto(schedule);
        var allowedImm = item.Imms.Any(i => i.ImmId == task.ImmId);
        if (!allowedImm || !ShiftTaskWindow.Belongs(
                task, dto.PlannedStartAt, item.Shift.StartAtUtc, item.Shift.EndAtUtc, Now))
            throw new ShiftNotFoundException("Задание не найдено в этой смене");

        var results = await _taskResults.GetAsync(taskId, ct)
            ?? throw new ShiftNotFoundException("Задание не найдено в этой смене");
        return new ShiftStaffingTaskDto { Task = dto, Results = results };
    }

    private async Task<ShiftStaffingItemDto> LoadAccessibleItemAsync(
        Guid itemId, StaffingCaller caller, CancellationToken ct)
    {
        var item = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Include(i => i.ShiftLead)
            .Include(i => i.OperatorAssignments).ThenInclude(a => a.Operator)
            .SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new ShiftNotFoundException("Смена не найдена");

        var now = Now;
        var nearestCandidates = await _context.ShiftPlanItems
            .AsNoTracking()
            .Where(i => i.ShiftLeadId != null)
            .ToListAsync(ct);

        bool canEditOperators;
        if (caller.IsManagerOrAdmin)
        {
            canEditOperators = now < item.EndAtUtc &&
                NearestShiftSelector.IsNearestForManager(nearestCandidates, item, now);
        }
        else if (caller.IsShiftLead)
        {
            var nearest = NearestShiftSelector.ForLead(nearestCandidates, caller.UserId, now);
            var isNearest = nearest?.Id == item.Id;
            if (item.ShiftLeadId != caller.UserId || (!isNearest && item.EndAtUtc > now))
                throw new ShiftAccessException("Старшему доступны только ближайшая и собственные исторические смены");
            canEditOperators = isNearest && CanEditNearestForLead(item, caller.UserId, now);
        }
        else
        {
            throw new ShiftAccessException("Недостаточно прав для чтения смены");
        }

        var summary = ToSummary(item, now, canEditOperators);
        return new ShiftStaffingItemDto
        {
            Shift = summary,
            ShiftLeadId = item.ShiftLeadId ?? string.Empty,
            ShiftLeadName = item.ShiftLead?.FullName ?? string.Empty,
            Imms = await LoadImmsAsync(item, now, summary.State, ct)
        };
    }

    public async Task<IReadOnlyList<StaffPersonDto>> GetOperatorsAsync(CancellationToken ct)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Operator)
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .ToListAsync(ct);
        return users.Select(u => new StaffPersonDto(u.Id, u.FullName, u.EmployeeId)).ToList();
    }

    private async Task<List<ShiftStaffingImmDto>> LoadImmsAsync(
        ShiftPlanItem item, DateTime now, ShiftState state, CancellationToken ct)
    {
        var shiftStart = Utc(item.StartAtUtc);
        var shiftEnd = Utc(item.EndAtUtc);
        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var plannedFrom = DateOnly.FromDateTime(shiftStart.AddDays(-1));
        var plannedTo = DateOnly.FromDateTime(shiftEnd.AddDays(1));

        var taskQuery = TaskQuery();
        var plannedCandidates = await taskQuery
            .Where(t => t.PlannedDate >= plannedFrom && t.PlannedDate <= plannedTo)
            .ToListAsync(ct);
        var activityCandidates = await taskQuery
            .Where(ShiftTaskWindow.ActivityOverlaps(shiftStart, shiftEnd))
            .ToListAsync(ct);

        var selectedTasks = plannedCandidates
            .Concat(activityCandidates)
            .GroupBy(t => t.Id)
            .Select(g => g.First())
            .Select(t => new TaskSelection(t, t.ToDto(schedule)))
            .Where(x => ShiftTaskWindow.Belongs(
                x.Entity, x.Dto.PlannedStartAt, shiftStart, shiftEnd, now))
            .OrderBy(x => x.Dto.PlannedStartAt)
            .ThenBy(x => x.Dto.Id)
            .ToList();

        var taskImmIds = selectedTasks.Select(t => t.Entity.ImmId).Distinct().ToList();
        var assignmentImmIds = item.OperatorAssignments.Select(a => a.ImmId).Distinct().ToList();
        var historical = state == ShiftState.Historical;
        var imms = await _context.Imms
            .AsNoTracking()
            .Where(i => i.IsActive || (historical &&
                (taskImmIds.Contains(i.Id) || assignmentImmIds.Contains(i.Id))))
            .OrderBy(i => i.Name).ThenBy(i => i.Id)
            .ToListAsync(ct);
        var immIds = imms.Select(i => i.Id).ToList();

        var activeTasks = historical
            ? new Dictionary<Guid, ActiveTaskStatus>()
            : await LoadActiveTaskStatusesAsync(immIds, ct);
        var openDowntimeIds = historical
            ? new HashSet<Guid>()
            : (await _context.Events
                .Where(e => immIds.Contains(e.ImmId) && e.EventType == EventType.Downtime && e.EndTime == null)
                .Select(e => e.ImmId)
                .Distinct()
                .ToListAsync(ct))
                .ToHashSet();
        var historyInputs = historical
            ? await _effectiveStatusHistory.GatherAsync(immIds, shiftStart, shiftEnd, shiftEnd, ct)
            : new Dictionary<Guid, EffectiveStatusInputs>();

        var tasksByImm = selectedTasks.GroupBy(t => t.Entity.ImmId)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Dto).ToList());
        var assignmentsByImm = item.OperatorAssignments.GroupBy(a => a.ImmId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.AssignedAt).ThenBy(a => a.Id).ToList());
        var result = new List<ShiftStaffingImmDto>(imms.Count);

        foreach (var imm in imms)
        {
            var assignments = assignmentsByImm.TryGetValue(imm.Id, out var immAssignments)
                ? immAssignments.Select(a => ToAssignmentDto(a, shiftStart, shiftEnd)).ToList()
                : new List<ShiftOperatorAssignmentDto>();
            result.Add(new ShiftStaffingImmDto
            {
                ImmId = imm.Id,
                Name = imm.Name,
                InventoryNumber = imm.InventoryNumber,
                IsActive = imm.IsActive,
                EffectiveStatus = historical
                    ? HistoricalEffectiveStatus(imm.Id, historyInputs, shiftStart, shiftEnd)
                    : LiveEffectiveStatus(imm.Id, activeTasks, openDowntimeIds, now),
                Tasks = tasksByImm.TryGetValue(imm.Id, out var tasks) ? tasks : new List<TaskDto>(),
                Assignments = assignments,
                ActiveOperatorCount = assignments.Count(a => a.UnassignedAt == null)
            });
        }

        return result;
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

    private async Task<Dictionary<Guid, ActiveTaskStatus>> LoadActiveTaskStatusesAsync(
        IReadOnlyCollection<Guid> immIds, CancellationToken ct)
    {
        var tasks = await _context.ShiftTasks
            .AsNoTracking()
            .Where(t => immIds.Contains(t.ImmId) &&
                (t.Status == TaskStatus.Setup || t.Status == TaskStatus.InProgress))
            .Select(t => new { t.ImmId, t.Status })
            .ToListAsync(ct);
        return tasks.GroupBy(t => t.ImmId).ToDictionary(
            g => g.Key,
            g => ActiveTaskStatusMap.From(g.Select(t => (TaskStatus?)t.Status).FirstOrDefault()));
    }

    private string LiveEffectiveStatus(
        Guid immId,
        IReadOnlyDictionary<Guid, ActiveTaskStatus> activeTasks,
        IReadOnlySet<Guid> openDowntimeIds,
        DateTime now)
    {
        var entry = _statusCache.GetEntry(immId);
        var rawStatus = entry?.Status ?? ImmStatus.Offline;
        var activeTask = activeTasks.TryGetValue(immId, out var task) ? task : ActiveTaskStatus.None;
        var thresholdPassed = entry != null &&
            (now - entry.SinceUtc).TotalSeconds >= _downtime.IdleThresholdSeconds;
        return ImmEffectiveStatus.Resolve(
            rawStatus, activeTask, openDowntimeIds.Contains(immId), thresholdPassed);
    }

    private static string HistoricalEffectiveStatus(
        Guid immId,
        IReadOnlyDictionary<Guid, EffectiveStatusInputs> historyInputs,
        DateTime shiftStart,
        DateTime shiftEnd)
    {
        if (!historyInputs.TryGetValue(immId, out var inputs)) return EffectiveStatus.Offline;
        var last = EffectiveStatusTimeline.Build(
            inputs.Raw, inputs.Tasks, inputs.Downtimes, shiftStart, shiftEnd).LastOrDefault();
        return last?.EffectiveStatus ?? EffectiveStatus.Offline;
    }

    private static ShiftOperatorAssignmentDto ToAssignmentDto(
        ShiftImmOperatorAssignment assignment, DateTime shiftStart, DateTime shiftEnd)
    {
        var interval = OperatorWorkIntervalPolicy.Resolve(
            assignment.AssignedAt, assignment.UnassignedAt, shiftStart, shiftEnd);
        return new ShiftOperatorAssignmentDto
        {
            Id = assignment.Id,
            ImmId = assignment.ImmId,
            OperatorId = assignment.OperatorId,
            OperatorName = assignment.Operator?.FullName ?? string.Empty,
            OperatorEmployeeId = assignment.Operator?.EmployeeId,
            Slot = assignment.Slot,
            AssignedAt = assignment.AssignedAt,
            UnassignedAt = assignment.UnassignedAt,
            WorkStart = interval.WorkStart,
            WorkEnd = interval.WorkEnd,
            IsWorkIntervalEmpty = interval.IsEmpty
        };
    }

    private static ShiftStaffingSummaryDto ToSummary(
        ShiftPlanItem item, DateTime now, bool canEditOperators) => new()
    {
        ItemId = item.Id,
        ShiftDate = item.Day.ShiftDate,
        Position = item.Position,
        StartAtUtc = item.StartAtUtc,
        EndAtUtc = item.EndAtUtc,
        TimeZoneId = item.TimeZoneId,
        State = ShiftTiming.Resolve(item.StartAtUtc, item.EndAtUtc, now),
        CanEditOperators = canEditOperators
    };

    private static bool CanEditNearestForLead(ShiftPlanItem item, string userId, DateTime now) =>
        item.ShiftLeadId == userId && !string.IsNullOrEmpty(item.ShiftLeadId) && now < item.EndAtUtc;

    private DateTime Now => Utc(_factoryClock.UtcNow);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record TaskSelection(ShiftTask Entity, TaskDto Dto);
}
