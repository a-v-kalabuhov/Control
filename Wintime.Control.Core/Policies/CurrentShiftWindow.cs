using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

/// <summary>Текущая смена, а вне рабочего интервала — последняя завершившаяся смена.</summary>
public static class CurrentShiftWindow
{
    public static Interval Resolve(
        DateTime utcNow,
        IReadOnlyCollection<Shift> shifts,
        TimeZoneInfo factoryTimeZone)
    {
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, factoryTimeZone);
        var localDate = localNow.Date;

        var candidates = shifts.SelectMany(shift => new[] { -1, 0 }.Select(dayOffset =>
        {
            var start = localDate.AddDays(dayOffset).AddMinutes(shift.StartMinutes);
            return new ShiftCandidate(start, start.AddMinutes(shift.DurationMinutes));
        })).ToList();

        var selected = candidates.FirstOrDefault(c => c.Start <= localNow && localNow < c.End)
            ?? candidates.Where(c => c.End <= localNow)
                .OrderByDescending(c => c.End)
                .FirstOrDefault();

        if (selected == null)
        {
            var start = localDate.AddHours(8);
            selected = new ShiftCandidate(start, start.AddHours(8));
        }

        return new Interval(ToUtc(selected.Start, factoryTimeZone), ToUtc(selected.End, factoryTimeZone));
    }

    private static DateTime ToUtc(DateTime local, TimeZoneInfo factoryTimeZone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (factoryTimeZone.IsInvalidTime(local))
            local = local.AddMinutes(15);
        return TimeZoneInfo.ConvertTimeToUtc(local, factoryTimeZone);
    }

    private sealed record ShiftCandidate(DateTime Start, DateTime End);
}
