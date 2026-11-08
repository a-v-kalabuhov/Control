using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

public sealed record ShiftWindow(int Position, DateTime StartAtUtc, DateTime EndAtUtc, string TimeZoneId);

public static class ShiftWindowCalculator
{
    public static IReadOnlyList<ShiftWindow> Build(DateOnly shiftDate, IReadOnlyCollection<Shift> shifts) =>
        shifts.OrderBy(s => s.StartMinutes).Select((shift, index) =>
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(shift.TimeZoneId);
            var localStart = shiftDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified)
                .AddMinutes(shift.StartMinutes);
            var localEnd = localStart.AddMinutes(shift.DurationMinutes);
            return new ShiftWindow(index + 1, ToUtc(localStart, tz), ToUtc(localEnd, tz), shift.TimeZoneId);
        }).ToList();

    private static DateTime ToUtc(DateTime local, TimeZoneInfo tz)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (tz.IsInvalidTime(local)) local = local.AddMinutes(15);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, tz), DateTimeKind.Utc);
    }
}
