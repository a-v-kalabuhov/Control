namespace Wintime.Control.Core.Policies;

public enum ShiftState { Future, Current, Historical }

public static class ShiftTiming
{
    public static ShiftState Resolve(DateTime startAtUtc, DateTime endAtUtc, DateTime nowUtc)
    {
        startAtUtc = DateTime.SpecifyKind(startAtUtc, DateTimeKind.Utc);
        endAtUtc = DateTime.SpecifyKind(endAtUtc, DateTimeKind.Utc);
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        if (nowUtc < startAtUtc) return ShiftState.Future;
        return nowUtc < endAtUtc ? ShiftState.Current : ShiftState.Historical;
    }
}
