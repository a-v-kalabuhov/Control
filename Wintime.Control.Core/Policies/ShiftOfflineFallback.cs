using Wintime.Control.Core.Constants;

namespace Wintime.Control.Core.Policies;

/// <summary>Определяет необходимость ручного ввода по фактическим сырым интервалам Offline.</summary>
public static class ShiftOfflineFallback
{
    public static bool IsRequired(
        IEnumerable<RawSegment> raw,
        Interval? taskActivity,
        DateTime shiftStartUtc,
        DateTime factEndUtc,
        bool hasOperators)
    {
        if (!hasOperators || taskActivity is null)
            return false;

        shiftStartUtc = DateTime.SpecifyKind(shiftStartUtc, DateTimeKind.Utc);
        factEndUtc = DateTime.SpecifyKind(factEndUtc, DateTimeKind.Utc);
        var activityStart = DateTime.SpecifyKind(taskActivity.Start, DateTimeKind.Utc);
        var activityEnd = DateTime.SpecifyKind(taskActivity.End, DateTimeKind.Utc);
        var start = activityStart > shiftStartUtc ? activityStart : shiftStartUtc;
        var end = activityEnd < factEndUtc ? activityEnd : factEndUtc;
        if (end <= start)
            return false;

        return raw.Any(segment =>
            segment.Status == ImmStatus.Offline
            && DateTime.SpecifyKind(segment.Start, DateTimeKind.Utc) < end
            && DateTime.SpecifyKind(segment.End, DateTimeKind.Utc) > start);
    }
}
