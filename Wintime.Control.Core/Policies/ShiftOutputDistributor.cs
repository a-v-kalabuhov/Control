namespace Wintime.Control.Core.Policies;

public sealed record ShiftOperatorSlice(
    Guid AssignmentId,
    string OperatorId,
    string FullName,
    string? EmployeeId,
    int Slot,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record ShiftOutputCycle(Guid TaskId, DateTime EndUtc, int Quantity);

public sealed record MergedShiftInterval(
    DateTime StartUtc,
    DateTime EndUtc,
    IReadOnlyList<ShiftOperatorSlice> Operators);

public sealed record ShiftOutputShareModel(
    Guid TaskId,
    DateTime StartUtc,
    DateTime EndUtc,
    string OperatorId,
    string FullName,
    string? EmployeeId,
    int SlotAtIntervalStart,
    int Quantity);

public sealed record ShiftOrphanOutputModel(
    Guid TaskId,
    DateTime StartUtc,
    DateTime EndUtc,
    int Quantity);

public sealed record ShiftOutputDistribution(
    IReadOnlyList<MergedShiftInterval> MergedIntervals,
    IReadOnlyList<ShiftOutputShareModel> Shares,
    IReadOnlyList<ShiftOrphanOutputModel> Orphans);

/// <summary>Распределение автоматического выпуска смены по объединённым интервалам литейщиков.</summary>
public static class ShiftOutputDistributor
{
    public static ShiftOutputDistribution Distribute(
        IReadOnlyList<ShiftOperatorSlice> assignments,
        IReadOnlyList<ShiftOutputCycle> cycles,
        IReadOnlyDictionary<Guid, Interval?> taskWindows,
        DateTime shiftStartUtc,
        DateTime factEndUtc)
    {
        if (factEndUtc <= shiftStartUtc)
            return EmptyDistribution();

        var clipped = assignments
            .Select(a => a with
            {
                StartUtc = a.StartUtc > shiftStartUtc ? a.StartUtc : shiftStartUtc,
                EndUtc = a.EndUtc < factEndUtc ? a.EndUtc : factEndUtc
            })
            .Where(a => a.EndUtc > a.StartUtc)
            .OrderBy(a => a.StartUtc)
            .ThenBy(a => a.Slot)
            .ThenBy(a => a.AssignmentId)
            .ToList();

        var boundaries = new SortedSet<DateTime> { shiftStartUtc, factEndUtc };
        foreach (var assignment in clipped)
        {
            boundaries.Add(assignment.StartUtc);
            boundaries.Add(assignment.EndUtc);
        }

        var points = boundaries.ToList();
        var merged = new List<MergedShiftInterval>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            var operators = clipped
                .Where(a => a.StartUtc <= start && a.EndUtc >= end)
                .GroupBy(a => a.OperatorId)
                .Select(g => g.OrderBy(a => a.Slot).ThenBy(a => a.AssignmentId).First())
                .OrderBy(a => a.Slot)
                .ThenBy(a => a.OperatorId, StringComparer.Ordinal)
                .ToList();
            var elementary = new MergedShiftInterval(start, end, operators);

            if (merged.Count > 0
                && merged[^1].EndUtc == start
                && SameOperatorSet(merged[^1].Operators, operators))
            {
                merged[^1] = merged[^1] with { EndUtc = end };
            }
            else
            {
                merged.Add(elementary);
            }
        }

        var cyclesByTask = cycles
            .Where(c => c.EndUtc >= shiftStartUtc && c.EndUtc < factEndUtc)
            .GroupBy(c => c.TaskId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var shares = new List<ShiftOutputShareModel>();
        var orphans = new List<ShiftOrphanOutputModel>();

        foreach (var (taskId, window) in taskWindows)
        {
            if (window == null)
                continue;

            cyclesByTask.TryGetValue(taskId, out var taskCycles);
            foreach (var interval in merged)
            {
                var start = interval.StartUtc > window.Start ? interval.StartUtc : window.Start;
                var end = interval.EndUtc < window.End ? interval.EndUtc : window.End;
                if (end <= start)
                    continue;

                var quantity = taskCycles?
                    .Where(c => c.EndUtc >= start && c.EndUtc < end)
                    .Sum(c => c.Quantity) ?? 0;
                AddDistribution(taskId, start, end, interval.Operators, quantity, shares, orphans);
            }
        }

        return new ShiftOutputDistribution(merged, shares, orphans);
    }

    private static bool SameOperatorSet(
        IReadOnlyList<ShiftOperatorSlice> first,
        IReadOnlyList<ShiftOperatorSlice> second) =>
        first.Select(o => o.OperatorId).Distinct().OrderBy(id => id, StringComparer.Ordinal)
            .SequenceEqual(second.Select(o => o.OperatorId).Distinct().OrderBy(id => id, StringComparer.Ordinal));

    private static void AddDistribution(
        Guid taskId,
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyList<ShiftOperatorSlice> operators,
        int quantity,
        List<ShiftOutputShareModel> shares,
        List<ShiftOrphanOutputModel> orphans)
    {
        if (operators.Count == 0)
        {
            if (quantity > 0)
                orphans.Add(new ShiftOrphanOutputModel(taskId, startUtc, endUtc, quantity));
            return;
        }

        if (operators.Count == 1)
        {
            shares.Add(Share(taskId, startUtc, endUtc, operators[0], quantity));
            return;
        }

        if (operators.Count == 2)
        {
            var ordered = operators.OrderBy(o => o.Slot).ThenBy(o => o.OperatorId, StringComparer.Ordinal).ToList();
            shares.Add(Share(taskId, startUtc, endUtc, ordered[0], (quantity + 1) / 2));
            shares.Add(Share(taskId, startUtc, endUtc, ordered[1], quantity / 2));
            return;
        }

        throw new InvalidOperationException("A merged shift interval cannot contain more than two operators.");
    }

    private static ShiftOutputShareModel Share(
        Guid taskId,
        DateTime startUtc,
        DateTime endUtc,
        ShiftOperatorSlice operatorSlice,
        int quantity) =>
        new(taskId, startUtc, endUtc, operatorSlice.OperatorId, operatorSlice.FullName,
            operatorSlice.EmployeeId, operatorSlice.Slot, quantity);

    private static ShiftOutputDistribution EmptyDistribution() =>
        new(Array.Empty<MergedShiftInterval>(), Array.Empty<ShiftOutputShareModel>(),
            Array.Empty<ShiftOrphanOutputModel>());
}
