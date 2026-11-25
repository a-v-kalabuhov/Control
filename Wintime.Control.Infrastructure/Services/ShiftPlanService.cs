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

    public ShiftPlanService(
        ControlDbContext context,
        IFactoryClock factoryClock,
        IShiftClosureIndicatorService closureIndicators)
    {
        _context = context;
        _factoryClock = factoryClock;
        _closureIndicators = closureIndicators;
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
            day ??= BuildVirtualDay(date, shifts);
            days.Add((day, isSaved));
            if (date == to) break;
        }

        var itemIds = days.Where(d => d.IsSaved).SelectMany(d => d.Day.Items).Select(i => i.Id).ToList();
        var indicators = await _closureIndicators.GetAsync(itemIds, now, ct);
        var nearestCandidates = BuildNearestCandidates(
            savedItems, days.Where(d => !d.IsSaved).Select(d => d.Day).ToList(),
            shifts, now, factoryToday, to, ct);
        var nearestByPosition = NearestByPosition(nearestCandidates, now);
        return days.Select(d => ToDto(
            d.Day, d.IsSaved, factoryToday, now, nearestByPosition, indicators)).ToList();
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
                if (isMaterializing)
                {
                    var shifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
                    day = BuildVirtualDay(date, shifts);
                }
                else
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT * FROM \"ShiftPlanDays\" WHERE \"Id\" = {dayId} FOR UPDATE", ct);
                    day = await _context.ShiftPlanDays
                        .Include(d => d.Items).ThenInclude(i => i.ShiftLead)
                        .Include(d => d.Items).ThenInclude(i => i.OperatorAssignments)
                        .Include(d => d.Items).ThenInclude(i => i.Closure)
                        .SingleOrDefaultAsync(d => d.Id == dayId, ct)
                        ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
                }

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

                User? lead = null;
                if (shiftLeadId != null)
                {
                    lead = await _context.Users.SingleOrDefaultAsync(u =>
                        u.Id == shiftLeadId && u.IsActive &&
                        (u.Role == UserRole.ShiftLead || u.Role == UserRole.Manager), ct);
                    if (lead == null)
                        throw new DomainException("Старшим смены может быть только активный старший смены или руководитель");
                }
                else if (item.Closure != null)
                {
                    throw new DomainException("Нельзя снять старшего смены при существующем сменном рапорте");
                }
                else if (item.OperatorAssignments.Count > 0)
                {
                    throw new DomainException("Нельзя снять старшего смены, пока у позиции есть интервалы литейщиков");
                }

                item.ShiftLeadId = lead?.Id;
                item.ShiftLead = lead;
                var deleteDay = shiftLeadId == null &&
                    day.Items.All(i => i.ShiftLeadId == null) &&
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
                    day = BuildVirtualDay(date, currentShifts);

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
                var nearestCandidates = BuildNearestCandidates(
                    savedItems, deleteDay ? [day] : [], currentShifts, now, factoryToday, date, ct);
                var nearestByPosition = NearestByPosition(nearestCandidates, now);
                var result = ToDto(
                    day, isSaved, factoryToday, now, nearestByPosition, indicators);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception exception) when (IsUniqueViolation(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
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

        var defaultPositions = (await _context.ShiftDefaultLeads
            .Select(d => d.Position)
            .ToListAsync(ct)).ToHashSet();

        foreach (var day in futureDays)
        {
            var windows = ShiftWindowCalculator.Build(day.ShiftDate, newSchedule);
            var removed = ShiftPlanSynchronizer.Apply(day, windows);
            if (removed.Any(i => i.Closure != null))
                throw new DomainException("Нельзя удалить позицию смены со сменным рапортом");
            _context.ShiftPlanItems.RemoveRange(removed);
            if (day.Items.All(i => i.ShiftLeadId == null && !defaultPositions.Contains(i.Position)))
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

    private static ShiftPlanDay BuildVirtualDay(DateOnly date, IReadOnlyCollection<Shift> shifts)
    {
        var day = new ShiftPlanDay { ShiftDate = date };
        foreach (var window in ShiftWindowCalculator.Build(date, shifts))
        {
            day.Items.Add(new ShiftPlanItem
            {
                Position = window.Position,
                StartAtUtc = window.StartAtUtc,
                EndAtUtc = window.EndAtUtc,
                TimeZoneId = window.TimeZoneId
            });
        }
        return day;
    }

    private static ShiftPlanDayDto ToDto(
        ShiftPlanDay day,
        bool isSaved,
        DateOnly factoryToday,
        DateTime now,
        IReadOnlyDictionary<int, ShiftPlanItem> nearestByPosition,
        IReadOnlyDictionary<Guid, ShiftClosureIndicator> indicators) => new()
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
            return new ShiftPlanItemDto
            {
                Id = isSaved ? item.Id : null,
                Position = item.Position,
                StartAtUtc = item.StartAtUtc,
                EndAtUtc = item.EndAtUtc,
                TimeZoneId = item.TimeZoneId,
                ShiftLeadId = item.ShiftLeadId,
                ShiftLeadName = item.ShiftLead?.FullName,
                State = ShiftTiming.Resolve(item.StartAtUtc, item.EndAtUtc, now),
                CanEditLead = !isClosed,
                CanEditOperators = isSaved && item.ShiftLeadId != null && !isClosed &&
                    (ended || isNearest),
                OperatorAssignmentCount = item.OperatorAssignments.Count,
                ClosureStatus = indicator?.ClosureStatus,
                HasOrphanOutput = indicator?.HasOrphanOutput ?? false,
                HasMissingLeadWarning = item.ShiftLeadId == null && isNearest
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

    private static List<ShiftPlanItem> BuildNearestCandidates(
        IReadOnlyCollection<ShiftPlanItem> savedItems,
        IReadOnlyCollection<ShiftPlanDay> requestedVirtualDays,
        IReadOnlyCollection<Shift> shifts,
        DateTime now,
        DateOnly factoryToday,
        DateOnly to,
        CancellationToken ct)
    {
        var candidates = savedItems.Concat(requestedVirtualDays.SelectMany(d => d.Items)).ToList();
        if (to < factoryToday || shifts.Count == 0)
            return candidates;

        var positions = ShiftWindowCalculator.Build(factoryToday, shifts)
            .Select(w => w.Position).ToHashSet();
        var allPositionsCurrent = positions.All(position => candidates.Any(i =>
            i.Position == position && DateTime.SpecifyKind(i.StartAtUtc, DateTimeKind.Utc) <= now
            && now < DateTime.SpecifyKind(i.EndAtUtc, DateTimeKind.Utc)));
        if (allPositionsCurrent)
            return candidates;

        var savedDates = savedItems.Select(i => i.Day.ShiftDate).ToHashSet();
        var virtualDates = requestedVirtualDays.Select(d => d.ShiftDate).ToHashSet();
        for (var date = factoryToday; ; date = date.AddDays(1))
        {
            ct.ThrowIfCancellationRequested();
            if (savedDates.Contains(date))
                continue;
            var day = virtualDates.Contains(date)
                ? requestedVirtualDays.First(d => d.ShiftDate == date)
                : BuildVirtualDay(date, shifts);
            if (!virtualDates.Contains(date))
                candidates.AddRange(day.Items);
            if (day.Items.Any(i => DateTime.SpecifyKind(i.EndAtUtc, DateTimeKind.Utc) > now))
                break;
        }
        return candidates;
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
