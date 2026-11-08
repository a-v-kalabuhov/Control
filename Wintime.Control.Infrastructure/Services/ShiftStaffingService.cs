using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
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
    private const string ConcurrencyMessage = "Состав литейщиков изменился. Обновите данные смены";

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

    public async Task<ShiftOperatorAssignmentDto> AssignAsync(
        Guid itemId, AssignOperatorRequest request, StaffingCaller caller, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var exists = await _context.ShiftPlanItems
                    .AsNoTracking()
                    .AnyAsync(i => i.Id == itemId, ct);
                if (!exists) throw new ShiftNotFoundException("Смена не найдена");

                // Тот же порядок блокировок, что при смене графика и замене старшего:
                // после ожидания все проверки выполняются по актуальной версии позиции.
                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await LockItemAsync(itemId, ct);
                var now = Now;
                await ValidateMutationAccessAsync(item, caller, now, ct);

                var imm = await _context.Imms.SingleOrDefaultAsync(i => i.Id == request.ImmId, ct)
                    ?? throw new ShiftNotFoundException("ТПА не найден");
                if (!imm.IsActive)
                    throw new DomainException("Нельзя назначить литейщика на архивный ТПА");

                var operatorUser = await _context.Users.SingleOrDefaultAsync(
                    u => u.Id == request.OperatorId, ct)
                    ?? throw new ShiftNotFoundException("Литейщик не найден");
                if (!operatorUser.IsActive || operatorUser.Role != UserRole.Operator)
                    throw new DomainException("Можно назначить только активного литейщика");

                var activeAssignments = item.OperatorAssignments
                    .Where(a => a.ImmId == request.ImmId && a.UnassignedAt == null)
                    .ToList();
                if (activeAssignments.Any(a => a.OperatorId == request.OperatorId))
                    throw new DomainException("Этот литейщик уже назначен на выбранный ТПА");

                var usedSlots = activeAssignments.Select(a => a.Slot).ToHashSet();
                var slot = new[] { 1, 2 }.FirstOrDefault(s => !usedSlots.Contains(s));
                if (slot == 0)
                    throw new DomainException("На один ТПА можно назначить не более двух литейщиков");

                var assignment = new ShiftImmOperatorAssignment
                {
                    ShiftPlanItemId = item.Id,
                    ImmId = request.ImmId,
                    OperatorId = request.OperatorId,
                    Operator = operatorUser,
                    Slot = slot,
                    AssignedAt = now
                };
                _context.ShiftImmOperatorAssignments.Add(assignment);
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return ToAssignmentDto(assignment, item.StartAtUtc, item.EndAtUtc);
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    public async Task<ShiftOperatorAssignmentDto> UnassignAsync(
        Guid assignmentId, StaffingCaller caller, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initial = await _context.ShiftImmOperatorAssignments
                    .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == assignmentId, ct)
                    ?? throw new ShiftNotFoundException("Назначение литейщика не найдено");

                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await LockItemAsync(initial.ShiftPlanItemId, ct);
                var now = Now;
                await ValidateMutationAccessAsync(item, caller, now, ct);
                var assignment = item.OperatorAssignments.SingleOrDefault(a => a.Id == assignmentId)
                    ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
                if (assignment.UnassignedAt != null)
                    throw new DomainException("Литейщик уже снят с этого ТПА");

                assignment.UnassignedAt = now;
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return ToAssignmentDto(assignment, item.StartAtUtc, item.EndAtUtc);
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    private async Task<ShiftPlanItem> LockItemAsync(Guid itemId, CancellationToken ct) =>
        await _context.ShiftPlanItems
            .FromSqlInterpolated(
                $"SELECT * FROM \"ShiftPlanItems\" WHERE \"Id\" = {itemId} FOR UPDATE")
            .Include(i => i.Day)
            .Include(i => i.ShiftLead)
            .Include(i => i.OperatorAssignments).ThenInclude(a => a.Operator)
            .SingleOrDefaultAsync(ct)
        ?? throw new ShiftConcurrencyException(ConcurrencyMessage);

    private async Task ValidateMutationAccessAsync(
        ShiftPlanItem item, StaffingCaller caller, DateTime now, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.ShiftLeadId))
            throw new DomainException("Сначала назначьте старшего смены");
        if (now >= Utc(item.EndAtUtc))
            throw new DomainException("Нельзя изменять состав литейщиков в исторической смене");

        var nearestCandidates = await _context.ShiftPlanItems
            .AsNoTracking()
            .Where(i => i.ShiftLeadId != null)
            .ToListAsync(ct);
        if (caller.IsManagerOrAdmin)
        {
            if (!NearestShiftSelector.IsNearestForManager(nearestCandidates, item, now))
                throw new ShiftAccessException("Руководителю доступно изменение только ближайшей смены");
            return;
        }
        if (caller.IsShiftLead)
        {
            var nearest = NearestShiftSelector.ForLead(nearestCandidates, caller.UserId, now);
            if (item.ShiftLeadId != caller.UserId || nearest?.Id != item.Id)
                throw new ShiftAccessException("Старшему доступно изменение только собственной ближайшей смены");
            return;
        }
        throw new ShiftAccessException("Недостаточно прав для изменения состава смены");
    }

    private static bool IsConcurrencyFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                (postgres.SqlState == PostgresErrorCodes.UniqueViolation ||
                 postgres.SqlState == PostgresErrorCodes.SerializationFailure ||
                 postgres.SqlState == PostgresErrorCodes.DeadlockDetected))
                return true;
        }
        return false;
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
