using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Reports;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftPlanService : IShiftPlanService
{
    private const string ConcurrencyMessage = "План смены уже изменён другим пользователем";
    private const int MaxRangeDays = 366;

    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IShiftClosureIndicatorService _closureIndicators;
    private readonly IShiftLeadFallbackProvider _fallbackProvider;
    private readonly IShiftPlanMaterializer _materializer;

    public ShiftPlanService(
        ControlDbContext context,
        IFactoryClock factoryClock,
        IShiftClosureIndicatorService closureIndicators,
        IShiftLeadFallbackProvider fallbackProvider,
        IShiftPlanMaterializer materializer)
    {
        _context = context;
        _factoryClock = factoryClock;
        _closureIndicators = closureIndicators;
        _fallbackProvider = fallbackProvider;
        _materializer = materializer;
    }

    public async Task<IReadOnlyList<ShiftPlanDayDto>> GetAsync(
        DateOnly from, DateOnly to, string _, CancellationToken ct)
    {
        if (from > to)
            throw new DomainException("Дата начала диапазона не может быть позже даты окончания");
        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
            throw new DomainException($"Диапазон плана смен не может превышать {MaxRangeDays} дней");

        var now = DateTime.SpecifyKind(_factoryClock.UtcNow, DateTimeKind.Utc);
        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var factoryToday = DateOnly.FromDateTime(schedule.ToLocal(now));
        var shifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
        var savedDays = await _context.ShiftPlanDays
            .AsNoTracking()
            .Where(d => d.ShiftDate >= from && d.ShiftDate <= to)
            .Include(d => d.Items).ThenInclude(i => i.ShiftLead)
            .Include(d => d.Items).ThenInclude(i => i.OperatorAssignments)
            .OrderBy(d => d.ShiftDate)
            .ToListAsync(ct);
        var savedItems = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .ToListAsync(ct);
        var savedByDate = savedDays.ToDictionary(d => d.ShiftDate);
        var days = new List<(ShiftPlanDay Day, bool IsSaved)>();

        for (var date = from; ; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            var isSaved = savedByDate.TryGetValue(date, out var day);
            day ??= ShiftPlanCandidateBuilder.BuildVirtualDay(date, shifts);
            days.Add((day, isSaved));
            if (date == to) break;
        }

        var itemIds = days.Where(d => d.IsSaved).SelectMany(d => d.Day.Items).Select(i => i.Id).ToList();
        var indicators = await _closureIndicators.GetAsync(itemIds, now, ct);
        var fallback = await _fallbackProvider.GetAsync(ct);
        var nearestCandidates = ShiftPlanCandidateBuilder.BuildNearestCandidates(
            savedItems, days.Where(d => !d.IsSaved).Select(d => d.Day).ToList(),
            shifts, now, factoryToday, to, ct);
        var nearestByPosition = NearestByPosition(nearestCandidates, now);
        return days.Select(d => ToDto(
            d.Day, d.IsSaved, factoryToday, now, nearestByPosition, indicators, fallback)).ToList();
    }

    public async Task<ShiftPlanDayDto> SetLeadAsync(
        DateOnly date, int position, string? shiftLeadId, string _, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initialDayId = await _context.ShiftPlanDays
                    .Where(d => d.ShiftDate == date)
                    .Select(d => d.Id)
                    .SingleOrDefaultAsync(ct);
                var initialItem = await _context.ShiftPlanItems
                    .AsNoTracking()
                    .Where(i => i.Day.ShiftDate == date && i.Position == position)
                    .Select(i => new
                    {
                        i.Id,
                        i.ShiftLeadId,
                        ClosureState = i.Closure == null ? (ShiftClosureState?)null : i.Closure.State
                    })
                    .SingleOrDefaultAsync(ct);
                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var now = DateTime.SpecifyKind(_factoryClock.UtcNow, DateTimeKind.Utc);
                var dayId = await _context.ShiftPlanDays
                    .Where(d => d.ShiftDate == date)
                    .Select(d => d.Id)
                    .SingleOrDefaultAsync(ct);
                if (dayId != initialDayId)
                    throw new ShiftConcurrencyException(ConcurrencyMessage);

                ShiftPlanDay day;
                var isMaterializing = dayId == Guid.Empty;
                day = await _materializer.LoadTrackedDayForUpdateAsync(date, ct);

                var item = day.Items.SingleOrDefault(i => i.Position == position)
                    ?? throw new ShiftNotFoundException("Позиция смены не найдена");
                if (!isMaterializing &&
                    (initialItem == null || item.Id != initialItem.Id ||
                     item.ShiftLeadId != initialItem.ShiftLeadId ||
                     item.Closure?.State != initialItem.ClosureState))
                    throw new ShiftConcurrencyException(ConcurrencyMessage);

                var started = now >= DateTime.SpecifyKind(item.StartAtUtc, DateTimeKind.Utc);
                if (started && item.Closure?.State == ShiftClosureState.Closed)
                    throw new DomainException("Старшего можно изменить только в незакрытой смене");

                var fallback = await _fallbackProvider.GetAsync(ct);
                User? lead = null;
                if (shiftLeadId != null)
                {
                    lead = await _context.Users.SingleOrDefaultAsync(u =>
                        u.Id == shiftLeadId && u.IsActive &&
                        (u.Role == UserRole.ShiftLead || u.Role == UserRole.Manager), ct);
                    if (lead == null)
                        throw new DomainException("Старшим смены может быть только активный старший смены или руководитель");
                }
                else
                {
                    var effectiveAfterClear = EffectiveShiftLeadResolver.Resolve(
                        new ShiftPlanItem { Position = item.Position }, fallback);
                    if (effectiveAfterClear.Id == null && item.Closure != null)
                        throw new DomainException("Нельзя снять старшего смены при существующем сменном рапорте");
                    if (effectiveAfterClear.Id == null && item.OperatorAssignments.Count > 0)
                        throw new DomainException("Нельзя снять старшего смены, пока у позиции есть интервалы литейщиков");
                }

                item.ShiftLeadId = lead?.Id;
                item.ShiftLead = lead;
                var deleteDay = shiftLeadId == null &&
                    day.Items.All(i => EffectiveShiftLeadResolver.Resolve(i, fallback).Id == null) &&
                    day.Items.All(i => i.OperatorAssignments.Count == 0) &&
                    day.Items.All(i => i.Closure == null);

                if (isMaterializing)
                {
                    if (!deleteDay) _context.ShiftPlanDays.Add(day);
                }
                else if (deleteDay)
                {
                    _context.ShiftPlanDays.Remove(day);
                }

                await _context.SaveChangesAsync(ct);

                var currentShifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
                if (deleteDay && !isMaterializing)
                    day = ShiftPlanCandidateBuilder.BuildVirtualDay(date, currentShifts);

                var savedItems = await _context.ShiftPlanItems
                    .AsNoTracking()
                    .Include(i => i.Day)
                    .ToListAsync(ct);
                var schedule = await _factoryClock.GetScheduleAsync(ct);
                var factoryToday = DateOnly.FromDateTime(schedule.ToLocal(now));
                var isSaved = !deleteDay;
                var indicators = isSaved
                    ? await _closureIndicators.GetAsync(day.Items.Select(i => i.Id).ToList(), now, ct)
                    : new Dictionary<Guid, ShiftClosureIndicator>();
                var nearestCandidates = ShiftPlanCandidateBuilder.BuildNearestCandidates(
                    savedItems, deleteDay ? [day] : [], currentShifts, now, factoryToday, date, ct);
                var nearestByPosition = NearestByPosition(nearestCandidates, now);
                var result = ToDto(
                    day, isSaved, factoryToday, now, nearestByPosition, indicators, fallback);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception exception) when (IsUniqueViolation(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    public async Task<ShiftPlanDayDto> MaterializeAsync(
        DateOnly date, int position, string _, CancellationToken ct)
    {
        var day = await _materializer.MaterializeAsync(date, position, ct);
        var now = DateTime.SpecifyKind(_factoryClock.UtcNow, DateTimeKind.Utc);
        var shifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var factoryToday = DateOnly.FromDateTime(schedule.ToLocal(now));
        var fallback = await _fallbackProvider.GetAsync(ct);
        var savedItems = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .ToListAsync(ct);
        var indicators = await _closureIndicators.GetAsync(
            day.Items.Select(i => i.Id).ToList(), now, ct);
        var nearestCandidates = ShiftPlanCandidateBuilder.BuildNearestCandidates(
            savedItems, [], shifts, now, factoryToday, date, ct);
        var nearestByPosition = NearestByPosition(nearestCandidates, now);
        return ToDto(day, true, factoryToday, now, nearestByPosition, indicators, fallback);
    }

    public async Task SynchronizeFutureDaysAsync(
        IReadOnlyList<Shift> newSchedule, DateTime nowUtc, CancellationToken ct)
    {
        var first = newSchedule.OrderBy(s => s.StartMinutes).FirstOrDefault();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            first?.TimeZoneId ?? FactoryCalendar.DefaultTimeZoneId);
        var schedule = new FactorySchedule(timeZone, first?.StartMinutes);
        var factoryToday = DateOnly.FromDateTime(schedule.ToLocal(nowUtc));
        var futureDays = await _context.ShiftPlanDays
            .Where(d => d.ShiftDate > factoryToday)
            .Include(d => d.Items).ThenInclude(i => i.OperatorAssignments)
            .Include(d => d.Items).ThenInclude(i => i.Closure)
            .ToListAsync(ct);

        var fallback = await _fallbackProvider.GetAsync(ct);

        foreach (var day in futureDays)
        {
            var windows = ShiftWindowCalculator.Build(day.ShiftDate, newSchedule);
            var removed = ShiftPlanSynchronizer.Apply(day, windows);
            if (removed.Any(i => i.Closure != null))
                throw new DomainException("Нельзя удалить позицию смены со сменным рапортом");
            _context.ShiftPlanItems.RemoveRange(removed);
            if (day.Items.All(i =>
                    EffectiveShiftLeadResolver.Resolve(i, fallback).Id == null &&
                    !i.OperatorAssignments.Any() && i.Closure == null))
                _context.ShiftPlanDays.Remove(day);
        }
    }

    public async Task<IReadOnlyList<StaffPersonDto>> GetLeadCandidatesAsync(CancellationToken ct)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive && (u.Role == UserRole.ShiftLead || u.Role == UserRole.Manager))
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .ToListAsync(ct);
        return users.Select(u => new StaffPersonDto(u.Id, u.FullName, u.EmployeeId)).ToList();
    }

    private static ShiftPlanDayDto ToDto(
        ShiftPlanDay day,
        bool isSaved,
        DateOnly factoryToday,
        DateTime now,
        IReadOnlyDictionary<int, ShiftPlanItem> nearestByPosition,
        IReadOnlyDictionary<Guid, ShiftClosureIndicator> indicators,
        ShiftLeadFallbackSnapshot fallback) => new()
    {
        ShiftDate = day.ShiftDate,
        IsSaved = isSaved,
        IsFactoryToday = day.ShiftDate == factoryToday,
        Items = day.Items.OrderBy(i => i.Position).Select(item =>
        {
            var indicator = isSaved ? indicators.GetValueOrDefault(item.Id) : null;
            var isNearest = nearestByPosition.TryGetValue(item.Position, out var nearest)
                && nearest.Id == item.Id;
            var isClosed = indicator?.ClosureStatus == ShiftClosureStatus.Closed;
            var ended = now >= DateTime.SpecifyKind(item.EndAtUtc, DateTimeKind.Utc);
            var effectiveLead = EffectiveShiftLeadResolver.Resolve(item, fallback);
            var effectiveLeadName = effectiveLead.Id != null &&
                fallback.Users.TryGetValue(effectiveLead.Id, out var effectiveLeadUser)
                    ? effectiveLeadUser.FullName
                    : null;
            return new ShiftPlanItemDto
            {
                Id = isSaved ? item.Id : null,
                Position = item.Position,
                StartAtUtc = item.StartAtUtc,
                EndAtUtc = item.EndAtUtc,
                TimeZoneId = item.TimeZoneId,
                ShiftLeadId = effectiveLead.Id,
                ShiftLeadName = effectiveLeadName,
                ShiftLeadSource = effectiveLead.Source,
                State = ShiftTiming.Resolve(item.StartAtUtc, item.EndAtUtc, now),
                CanEditLead = !isClosed,
                CanEditOperators = effectiveLead.Id != null && !isClosed &&
                    (ended || isNearest),
                OperatorAssignmentCount = item.OperatorAssignments.Count,
                ClosureStatus = indicator?.ClosureStatus,
                HasOrphanOutput = indicator?.HasOrphanOutput ?? false,
                HasMissingLeadWarning = effectiveLead.Id == null && isNearest
            };
        }).ToList()
    };

    private static Dictionary<int, ShiftPlanItem> NearestByPosition(
        IEnumerable<ShiftPlanItem> candidates, DateTime now)
    {
        return candidates.GroupBy(i => i.Position)
            .Select(group =>
            {
                var nearest = group
                    .Where(i => DateTime.SpecifyKind(i.StartAtUtc, DateTimeKind.Utc) <= now
                        && now < DateTime.SpecifyKind(i.EndAtUtc, DateTimeKind.Utc))
                    .OrderBy(i => i.StartAtUtc)
                    .FirstOrDefault()
                    ?? group.Where(i => now < DateTime.SpecifyKind(i.StartAtUtc, DateTimeKind.Utc))
                        .OrderBy(i => i.StartAtUtc)
                        .FirstOrDefault();
                return (group.Key, nearest);
            })
            .Where(x => x.nearest != null)
            .ToDictionary(x => x.Key, x => x.nearest!);
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }
        return false;
    }
}
