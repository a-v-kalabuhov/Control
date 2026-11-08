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

    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;

    public ShiftPlanService(ControlDbContext context, IFactoryClock factoryClock)
    {
        _context = context;
        _factoryClock = factoryClock;
    }

    public async Task<IReadOnlyList<ShiftPlanDayDto>> GetAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to)
            throw new DomainException("Дата начала диапазона не может быть позже даты окончания");

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
        var nearestCandidates = await _context.ShiftPlanItems
            .AsNoTracking()
            .Where(i => i.ShiftLeadId != null)
            .ToListAsync(ct);
        var savedByDate = savedDays.ToDictionary(d => d.ShiftDate);
        var result = new List<ShiftPlanDayDto>();

        for (var date = from; ; date = date.AddDays(1))
        {
            var isSaved = savedByDate.TryGetValue(date, out var day);
            day ??= BuildVirtualDay(date, shifts);
            result.Add(ToDto(day, isSaved, factoryToday, now, nearestCandidates));
            if (date == to) break;
        }

        return result;
    }

    public async Task<ShiftPlanDayDto> SetLeadAsync(
        DateOnly date, int position, string? shiftLeadId, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var now = DateTime.SpecifyKind(_factoryClock.UtcNow, DateTimeKind.Utc);
                var dayId = await _context.ShiftPlanDays
                    .Where(d => d.ShiftDate == date)
                    .Select(d => d.Id)
                    .SingleOrDefaultAsync(ct);

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
                        .SingleOrDefaultAsync(d => d.Id == dayId, ct)
                        ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
                }

                var item = day.Items.SingleOrDefault(i => i.Position == position)
                    ?? throw new ShiftNotFoundException("Позиция смены не найдена");
                if (now >= DateTime.SpecifyKind(item.StartAtUtc, DateTimeKind.Utc))
                    throw new DomainException("Старшего можно изменить только до начала смены");

                User? lead = null;
                if (shiftLeadId != null)
                {
                    lead = await _context.Users.SingleOrDefaultAsync(u =>
                        u.Id == shiftLeadId && u.IsActive &&
                        (u.Role == UserRole.ShiftLead || u.Role == UserRole.Manager), ct);
                    if (lead == null)
                        throw new DomainException("Старшим смены может быть только активный старший смены или руководитель");
                }
                else if (item.OperatorAssignments.Count > 0)
                {
                    throw new DomainException("Нельзя снять старшего смены, пока у позиции есть интервалы литейщиков");
                }

                item.ShiftLeadId = lead?.Id;
                item.ShiftLead = lead;
                var deleteDay = shiftLeadId == null &&
                    day.Items.All(i => i.ShiftLeadId == null) &&
                    day.Items.All(i => i.OperatorAssignments.Count == 0);

                if (isMaterializing)
                {
                    if (!deleteDay) _context.ShiftPlanDays.Add(day);
                }
                else if (deleteDay)
                {
                    _context.ShiftPlanDays.Remove(day);
                }

                await _context.SaveChangesAsync(ct);

                if (deleteDay && !isMaterializing)
                {
                    var currentShifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
                    day = BuildVirtualDay(date, currentShifts);
                }

                var nearestCandidates = await _context.ShiftPlanItems
                    .AsNoTracking()
                    .Where(i => i.ShiftLeadId != null)
                    .ToListAsync(ct);
                var schedule = await _factoryClock.GetScheduleAsync(ct);
                var factoryToday = DateOnly.FromDateTime(schedule.ToLocal(now));
                var isSaved = !deleteDay;
                var result = ToDto(day, isSaved, factoryToday, now, nearestCandidates);
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
            .ToListAsync(ct);

        foreach (var day in futureDays)
        {
            var windows = ShiftWindowCalculator.Build(day.ShiftDate, newSchedule);
            var removed = ShiftPlanSynchronizer.Apply(day, windows);
            _context.ShiftPlanItems.RemoveRange(removed);
            if (day.Items.All(i => i.ShiftLeadId == null))
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
        IReadOnlyCollection<ShiftPlanItem> nearestCandidates) => new()
    {
        ShiftDate = day.ShiftDate,
        IsSaved = isSaved,
        IsFactoryToday = day.ShiftDate == factoryToday,
        Items = day.Items.OrderBy(i => i.Position).Select(item => new ShiftPlanItemDto
        {
            Id = isSaved ? item.Id : null,
            Position = item.Position,
            StartAtUtc = item.StartAtUtc,
            EndAtUtc = item.EndAtUtc,
            TimeZoneId = item.TimeZoneId,
            ShiftLeadId = item.ShiftLeadId,
            ShiftLeadName = item.ShiftLead?.FullName,
            State = ShiftTiming.Resolve(item.StartAtUtc, item.EndAtUtc, now),
            CanEditLead = now < DateTime.SpecifyKind(item.StartAtUtc, DateTimeKind.Utc),
            CanEditOperators = isSaved &&
                NearestShiftSelector.IsNearestForManager(nearestCandidates, item, now),
            OperatorAssignmentCount = item.OperatorAssignments.Count
        }).ToList()
    };

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
