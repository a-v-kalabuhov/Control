namespace Wintime.Control.Core.Policies;

public sealed record OperatorWorkInterval(DateTime WorkStart, DateTime WorkEnd, bool IsEmpty);

public static class OperatorWorkIntervalPolicy
{
    public static OperatorWorkInterval Resolve(
        DateTime assignedAt, DateTime? unassignedAt, DateTime shiftStartAtUtc, DateTime shiftEndAtUtc)
    {
        var start = assignedAt > shiftStartAtUtc ? assignedAt : shiftStartAtUtc;
        var assignmentEnd = unassignedAt ?? shiftEndAtUtc;
        var end = assignmentEnd < shiftEndAtUtc ? assignmentEnd : shiftEndAtUtc;
        return new OperatorWorkInterval(start, end, end < start);
    }
}
