using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

public static class ShiftPlanCandidateBuilder
{
    public static ShiftPlanDay BuildVirtualDay(DateOnly date, IReadOnlyCollection<Shift> shifts)
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

    public static List<ShiftPlanItem> BuildNearestCandidates(
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
}
