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
    private const string NearestConcurrencyMessage = "Ближайшая смена изменилась. Обновите данные";

    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IImmStatusCache _statusCache;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;
    private readonly ITaskResultsService _taskResults;
    private readonly IShiftLeadFallbackProvider _fallbackProvider;
    private readonly IShiftPlanMaterializer _materializer;
    private readonly DowntimeSettings _downtime;

    public ShiftStaffingService(
        ControlDbContext context,
        IFactoryClock factoryClock,
        IImmStatusCache statusCache,
        IEffectiveStatusHistoryService effectiveStatusHistory,
        ITaskResultsService taskResults,
        IShiftLeadFallbackProvider fallbackProvider,
        IShiftPlanMaterializer materializer,
        IOptions<DowntimeSettings> downtime)
    {
        _context = context;
        _factoryClock = factoryClock;
        _statusCache = statusCache;
        _effectiveStatusHistory = effectiveStatusHistory;
        _taskResults = taskResults;
        _fallbackProvider = fallbackProvider;
        _materializer = materializer;
        _downtime = downtime.Value;
    }

    public async Task<NearestShiftDto> GetNearestAsync(string userId, CancellationToken ct)
    {
        var now = Now;
        var candidateSet = await LoadNearestCandidatesAsync(now, ct);
        var nearest = NearestShiftSelector.ForLead(
            candidateSet.Items, userId, now, item => EffectiveLeadId(item, candidateSet.Fallback));
        var fallback = candidateSet.Fallback;

        if (nearest?.ShiftPlanDayId == Guid.Empty)
        {
            var date = nearest.Day.ShiftDate;
            var position = nearest.Position;
            var selectedStartAtUtc = nearest.StartAtUtc;
            var selectedEndAtUtc = nearest.EndAtUtc;
            var selectedTimeZoneId = nearest.TimeZoneId;
            var materializedDay = await _materializer.MaterializeAsync(date, position, ct);
            var materializedItem = materializedDay.Items.Single(i => i.Position == position);
            if (materializedItem.StartAtUtc != selectedStartAtUtc ||
                materializedItem.EndAtUtc != selectedEndAtUtc ||
                materializedItem.TimeZoneId != selectedTimeZoneId)
                throw new ShiftConcurrencyException(NearestConcurrencyMessage);
            var materializedId = materializedItem.Id;

            // MaterializeAsync повторно читает график и default под ShiftScheduleLock.
            // После него повторяем сам выбор по свежему времени: устаревший itemId нельзя выдать клиенту.
            now = Now;
            var verified = await LoadNearestCandidatesAsync(now, ct);
            nearest = NearestShiftSelector.ForLead(
                verified.Items, userId, now, item => EffectiveLeadId(item, verified.Fallback));
            if (nearest?.Id != materializedId)
                throw new ShiftConcurrencyException(NearestConcurrencyMessage);
            fallback = verified.Fallback;
        }

        return new NearestShiftDto(nearest == null
            ? null
            : ToSummary(nearest, now,
                CanEditNearestForLead(nearest, EffectiveLeadId(nearest, fallback), now)));
    }

    public async Task<IReadOnlyList<ShiftStaffingSummaryDto>> GetHistoryAsync(
        string userId, CancellationToken ct)
    {
        var now = Now;
        var items = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Include(i => i.Closure)
            .Where(i => i.EndAtUtc <= now)
            .ToListAsync(ct);
        var fallback = await _fallbackProvider.GetAsync(ct);
        return items
            .Where(i => EffectiveLeadId(i, fallback) == userId)
            .OrderByDescending(i => i.StartAtUtc)
            .ThenByDescending(i => i.Id)
            .Select(i => ToSummary(i, now,
                CanEditEndedUnclosed(i, EffectiveLeadId(i, fallback), now)))
            .ToList();
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
            .Include(i => i.Closure)
            .Include(i => i.ShiftLead)
            .Include(i => i.OperatorAssignments).ThenInclude(a => a.Operator)
            .SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new ShiftNotFoundException("Смена не найдена");

        var now = Now;
        var candidateSet = await LoadNearestCandidatesAsync(now, ct);
        var effectiveLead = EffectiveShiftLeadResolver.Resolve(item, candidateSet.Fallback);

        var isClosed = item.Closure?.State == ShiftClosureState.Closed;
        var hasLead = effectiveLead.Id != null;
        bool canEditOperators;
        if (caller.IsManagerOrAdmin)
        {
            canEditOperators = hasLead && !isClosed &&
                (now >= item.EndAtUtc || NearestShiftSelector.IsNearestForManager(
                    candidateSet.Items, item, now,
                    candidate => EffectiveLeadId(candidate, candidateSet.Fallback)));
        }
        else if (caller.IsShiftLead)
        {
            var nearest = NearestShiftSelector.ForLead(
                candidateSet.Items, caller.UserId, now,
                candidate => EffectiveLeadId(candidate, candidateSet.Fallback));
            var isNearest = nearest?.Id == item.Id;
            if (effectiveLead.Id != caller.UserId || (!isNearest && item.EndAtUtc > now))
                throw new ShiftAccessException("Старшему доступны только ближайшая и собственные исторические смены");
            canEditOperators = !isClosed &&
                (CanEditNearestForLead(item, effectiveLead.Id, now) ||
                 CanEditEndedUnclosed(item, effectiveLead.Id, now));
        }
        else
        {
            throw new ShiftAccessException("Недостаточно прав для чтения смены");
        }

        var summary = ToSummary(item, now, canEditOperators);
        return new ShiftStaffingItemDto
        {
            Shift = summary,
            ShiftLeadId = effectiveLead.Id ?? string.Empty,
            ShiftLeadName = effectiveLead.Id != null &&
                candidateSet.Fallback.Users.TryGetValue(effectiveLead.Id, out var effectiveLeadUser)
                    ? effectiveLeadUser.FullName
                    : string.Empty,
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
        var assignedAt = NormalizeClientTime(request.AssignedAt);
        var unassignedAt = NormalizeClientTime(request.UnassignedAt);
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initial = await _context.ShiftPlanItems
                    .AsNoTracking()
                    .Include(i => i.Closure)
                    .SingleOrDefaultAsync(i => i.Id == itemId, ct)
                    ?? throw new ShiftNotFoundException("Смена не найдена");
                var initialClosureState = initial.Closure?.State;

                // Тот же порядок блокировок, что при смене графика и замене старшего:
                // после ожидания все проверки выполняются по актуальной версии позиции.
                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await LockItemAsync(itemId, ct);
                var now = Now;
                await ValidateMutationAccessAsync(item, caller, now, initialClosureState, ct);
                var start = assignedAt ?? Utc(item.StartAtUtc);
                var end = unassignedAt ?? Utc(item.EndAtUtc);
                ValidateIntervalBounds(start, end, item, allowPreStartCancel: unassignedAt.HasValue, now);

                // Общий порядок: schedule advisory -> ShiftPlanItem -> IMM/User.
                // Свежие no-tracking FOR UPDATE чтения не используют сущности, загруженные
                // через Include, а блокировки удерживаются транзакцией до SaveChanges.
                var imm = await _context.Imms
                    .FromSqlInterpolated(
                        $"SELECT * FROM \"Imms\" WHERE \"Id\" = {request.ImmId} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct)
                    ?? throw new ShiftNotFoundException("ТПА не найден");
                if (!imm.IsActive)
                    throw new DomainException("Нельзя назначить литейщика на архивный ТПА");

                var operatorUser = await _context.Users
                    .FromSqlInterpolated(
                        $"SELECT * FROM \"Users\" WHERE \"Id\" = {request.OperatorId} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync(ct)
                    ?? throw new ShiftNotFoundException("Литейщик не найден");
                if (!operatorUser.IsActive || operatorUser.Role != UserRole.Operator)
                    throw new DomainException("Можно назначить только активного литейщика");

                EnsureOperatorDoesNotOverlap(item, request.ImmId, request.OperatorId, start, end, Guid.Empty);
                var slot = SelectSlot(item, request.ImmId, start, end, Guid.Empty);

                var assignment = new ShiftImmOperatorAssignment
                {
                    ShiftPlanItemId = item.Id,
                    ImmId = request.ImmId,
                    OperatorId = request.OperatorId,
                    Slot = slot,
                    AssignedAt = start,
                    UnassignedAt = unassignedAt,
                    CreatedById = caller.UserId,
                    CreatedAt = now
                };
                _context.ShiftImmOperatorAssignments.Add(assignment);
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                var dto = ToAssignmentDto(assignment, item.StartAtUtc, item.EndAtUtc);
                dto.OperatorName = operatorUser.FullName;
                dto.OperatorEmployeeId = operatorUser.EmployeeId;
                return dto;
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    public async Task<ShiftOperatorAssignmentDto> UnassignAsync(
        Guid assignmentId, UnassignOperatorRequest request, StaffingCaller caller, CancellationToken ct)
    {
        var requestedEnd = NormalizeClientTime(request.UnassignedAt);
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initial = await _context.ShiftImmOperatorAssignments
                    .AsNoTracking()
                    .Include(a => a.ShiftPlanItem).ThenInclude(i => i.Closure)
                    .SingleOrDefaultAsync(a => a.Id == assignmentId, ct)
                    ?? throw new ShiftNotFoundException("Назначение литейщика не найдено");
                var initialClosureState = initial.ShiftPlanItem.Closure?.State;

                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await LockItemAsync(initial.ShiftPlanItemId, ct);
                var now = Now;
                await ValidateMutationAccessAsync(item, caller, now, initialClosureState, ct);
                var assignment = item.OperatorAssignments.SingleOrDefault(a => a.Id == assignmentId)
                    ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
                if (assignment.UnassignedAt != null)
                    throw new DomainException("Литейщик уже снят с этого ТПА");

                var interval = OperatorWorkIntervalPolicy.Resolve(
                    assignment.AssignedAt, null, item.StartAtUtc, item.EndAtUtc);
                var end = requestedEnd ?? OperatorWorkIntervalPolicy.DefaultUnassignedAt(
                    now, interval.WorkStart, Utc(item.EndAtUtc));
                ValidateIntervalEnd(end, interval.WorkStart, item, now);
                assignment.UnassignedAt = end;
                assignment.UpdatedById = caller.UserId;
                assignment.UpdatedAt = now;
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

    public async Task<ShiftOperatorAssignmentDto> UpdateIntervalAsync(
        Guid assignmentId, UpdateOperatorIntervalRequest request, StaffingCaller caller, CancellationToken ct)
    {
        var assignedAt = NormalizeClientTime(request.AssignedAt);
        var unassignedAt = NormalizeClientTime(request.UnassignedAt);
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initial = await _context.ShiftImmOperatorAssignments
                    .AsNoTracking()
                    .Include(a => a.ShiftPlanItem).ThenInclude(i => i.Closure)
                    .SingleOrDefaultAsync(a => a.Id == assignmentId, ct)
                    ?? throw new ShiftNotFoundException("Назначение литейщика не найдено");
                var initialClosureState = initial.ShiftPlanItem.Closure?.State;

                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await LockItemAsync(initial.ShiftPlanItemId, ct);
                var now = Now;
                await ValidateMutationAccessAsync(item, caller, now, initialClosureState, ct);
                var assignment = item.OperatorAssignments.SingleOrDefault(a => a.Id == assignmentId)
                    ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
                var end = unassignedAt ?? Utc(item.EndAtUtc);
                ValidateIntervalBounds(assignedAt, end, item, allowPreStartCancel: false, now);
                EnsureOperatorDoesNotOverlap(
                    item, assignment.ImmId, assignment.OperatorId, assignedAt, end, assignment.Id);
                assignment.Slot = SelectSlot(item, assignment.ImmId, assignedAt, end, assignment.Id);
                assignment.AssignedAt = assignedAt;
                assignment.UnassignedAt = unassignedAt;
                assignment.UpdatedById = caller.UserId;
                assignment.UpdatedAt = now;
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

    private static void ValidateIntervalBounds(
        DateTime start, DateTime end, ShiftPlanItem item, bool allowPreStartCancel, DateTime now)
    {
        var shiftStart = Utc(item.StartAtUtc);
        var shiftEnd = Utc(item.EndAtUtc);
        if (start < shiftStart || start > shiftEnd || end < shiftStart || end > shiftEnd)
            throw new DomainException("Границы работы литейщика должны находиться внутри смены");
        if (end < start)
            throw new DomainException("Окончание работы литейщика не может предшествовать началу");
        if (end == start && (!allowPreStartCancel || now >= start))
            throw new DomainException("Пустой интервал допустим только при отмене предварительного назначения");
    }

    private static void ValidateIntervalEnd(
        DateTime end, DateTime workStart, ShiftPlanItem item, DateTime now)
    {
        if (end < Utc(item.StartAtUtc) || end > Utc(item.EndAtUtc))
            throw new DomainException("Окончание работы литейщика должно находиться внутри смены");
        if (end < workStart)
            throw new DomainException("Окончание работы литейщика не может предшествовать началу");
        if (end == workStart && now >= workStart)
            throw new DomainException("Пустой интервал допустим только при отмене предварительного назначения");
    }

    private static void EnsureOperatorDoesNotOverlap(
        ShiftPlanItem item, Guid immId, string operatorId,
        DateTime start, DateTime end, Guid exceptId)
    {
        if (item.OperatorAssignments.Any(a =>
                a.ImmId == immId && a.OperatorId == operatorId && Overlaps(a, start, end, exceptId)))
            throw new ShiftConcurrencyException(
                "У литейщика уже есть пересекающийся интервал на этом ТПА. Измените границы");
    }

    private static int SelectSlot(
        ShiftPlanItem item, Guid immId, DateTime start, DateTime end, Guid exceptId)
    {
        var usedSlots = item.OperatorAssignments
            .Where(a => a.ImmId == immId && Overlaps(a, start, end, exceptId))
            .Select(a => a.Slot)
            .ToHashSet();
        var slot = new[] { 1, 2 }.FirstOrDefault(s => !usedSlots.Contains(s));
        if (slot == 0)
            throw new ShiftConcurrencyException(
                "Ни один слот не свободен на всём запрошенном интервале. Измените границы");
        return slot;
    }

    private static bool Overlaps(
        ShiftImmOperatorAssignment assignment, DateTime start, DateTime end, Guid exceptId) =>
        assignment.Id != exceptId &&
        start < end &&
        assignment.AssignedAt < (assignment.UnassignedAt ?? DateTime.MaxValue) &&
        assignment.AssignedAt < end &&
        (assignment.UnassignedAt == null || assignment.UnassignedAt > start);

    private static DateTime NormalizeClientTime(DateTime value)
    {
        if (value.Kind == DateTimeKind.Unspecified)
            throw new DomainException("Время должно содержать часовой пояс или смещение UTC");
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static DateTime? NormalizeClientTime(DateTime? value) =>
        value.HasValue ? NormalizeClientTime(value.Value) : null;

    private async Task<ShiftPlanItem> LockItemAsync(Guid itemId, CancellationToken ct) =>
        await _context.ShiftPlanItems
            .FromSqlInterpolated(
                $"SELECT * FROM \"ShiftPlanItems\" WHERE \"Id\" = {itemId} FOR UPDATE")
            .Include(i => i.Day)
            .Include(i => i.Closure)
            .Include(i => i.ShiftLead)
            .Include(i => i.OperatorAssignments).ThenInclude(a => a.Operator)
            .SingleOrDefaultAsync(ct)
        ?? throw new ShiftConcurrencyException(ConcurrencyMessage);

    private async Task ValidateMutationAccessAsync(
        ShiftPlanItem item, StaffingCaller caller, DateTime now,
        ShiftClosureState? initialClosureState, CancellationToken ct)
    {
        if (!caller.IsManagerOrAdmin && !caller.IsShiftLead)
            throw new ShiftAccessException("Недостаточно прав для изменения состава смены");

        var candidateSet = await LoadNearestCandidatesAsync(now, ct);
        var effectiveLeadId = EffectiveLeadId(item, candidateSet.Fallback);
        if (caller.IsShiftLead && effectiveLeadId != caller.UserId)
            throw new ShiftAccessException("Старшему доступно изменение только собственной смены");
        if (effectiveLeadId == null)
            throw new DomainException("Сначала назначьте старшего смены");

        var ended = now >= Utc(item.EndAtUtc);
        if (caller.IsManagerOrAdmin)
        {
            if (!ended && !NearestShiftSelector.IsNearestForManager(
                    candidateSet.Items, item, now,
                    candidate => EffectiveLeadId(candidate, candidateSet.Fallback)))
                throw new ShiftAccessException("Руководителю доступно изменение только ближайшей или завершённой незакрытой смены");
        }
        else if (!ended)
        {
            var nearest = NearestShiftSelector.ForLead(
                candidateSet.Items, caller.UserId, now,
                candidate => EffectiveLeadId(candidate, candidateSet.Fallback));
            if (nearest?.Id != item.Id)
                throw new ShiftAccessException("Старшему доступно изменение только собственной ближайшей смены");
        }

        if (item.Closure?.State == ShiftClosureState.Closed)
        {
            if (initialClosureState != ShiftClosureState.Closed)
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            throw new DomainException("shiftClosed");
        }
    }

    private async Task<(List<ShiftPlanItem> Items, ShiftLeadFallbackSnapshot Fallback)>
        LoadNearestCandidatesAsync(DateTime now, CancellationToken ct)
    {
        var savedItems = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Include(i => i.Closure)
            .ToListAsync(ct);
        var shifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var factoryToday = DateOnly.FromDateTime(schedule.ToLocal(now));
        var savedDates = savedItems.Select(i => i.Day.ShiftDate).ToHashSet();
        var requestedVirtualDays = new[] { factoryToday.AddDays(-1), factoryToday, factoryToday.AddDays(1) }
            .Where(date => !savedDates.Contains(date))
            .Select(date => ShiftPlanCandidateBuilder.BuildVirtualDay(date, shifts))
            .ToList();
        var candidates = ShiftPlanCandidateBuilder.BuildNearestCandidates(
            savedItems, requestedVirtualDays, shifts, now, factoryToday, factoryToday, ct);
        var fallback = await _fallbackProvider.GetAsync(ct);
        return (candidates, fallback);
    }

    private static string? EffectiveLeadId(
        ShiftPlanItem item, ShiftLeadFallbackSnapshot fallback) =>
        EffectiveShiftLeadResolver.Resolve(item, fallback).Id;

    private static bool IsConcurrencyFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                (postgres.SqlState == PostgresErrorCodes.UniqueViolation ||
                 postgres.SqlState == PostgresErrorCodes.ExclusionViolation ||
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
                ActiveOperatorCount = historical ? 0 : assignments.Count(a =>
                    a.WorkStart <= now && now < a.WorkEnd)
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
            CreatedById = assignment.CreatedById,
            CreatedAt = assignment.CreatedAt,
            UpdatedById = assignment.UpdatedById,
            UpdatedAt = assignment.UpdatedAt,
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

    private static bool CanEditNearestForLead(
        ShiftPlanItem item, string? effectiveLeadId, DateTime now) =>
        !string.IsNullOrEmpty(effectiveLeadId) &&
        item.Closure?.State != ShiftClosureState.Closed && now < item.EndAtUtc;

    private static bool CanEditEndedUnclosed(
        ShiftPlanItem item, string? effectiveLeadId, DateTime now) =>
        !string.IsNullOrEmpty(effectiveLeadId) &&
        item.Closure?.State != ShiftClosureState.Closed && now >= item.EndAtUtc;

    private DateTime Now => Utc(_factoryClock.UtcNow);

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record TaskSelection(ShiftTask Entity, TaskDto Dto);
}
