using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

public sealed record ShiftCycleMetrics(int OutputQuantity, double? AverageFullCycleSeconds);

/// <summary>Выпуск и средний полный цикл по успешным закрытым циклам задания.</summary>
public static class ShiftClosureCycleMetrics
{
    public static ShiftCycleMetrics Calculate(IEnumerable<ImmCycle> cycles, Guid taskId,
        DateTime shiftStartUtc, DateTime factEndUtc)
    {
        var selected = SuccessfulClosedCycles(cycles, taskId)
            .Where(c => c.EndTime!.Value >= shiftStartUtc && c.EndTime.Value < factEndUtc)
            .ToList();

        return new ShiftCycleMetrics(
            selected.Sum(OutputQuantity),
            selected.Count == 0 ? null : selected.Average(c => c.DurationSeconds));
    }

    public static int PreviousOutput(IEnumerable<ImmCycle> cycles, Guid taskId, DateTime shiftStartUtc) =>
        SuccessfulClosedCycles(cycles, taskId)
            .Where(c => c.EndTime!.Value < shiftStartUtc)
            .Sum(OutputQuantity);

    private static IEnumerable<ImmCycle> SuccessfulClosedCycles(IEnumerable<ImmCycle> cycles, Guid taskId) =>
        cycles.Where(c => c.TaskId == taskId && c.IsSuccessful && c.EndTime != null);

    private static int OutputQuantity(ImmCycle cycle) =>
        cycle.Cavities > 0 ? cycle.Cavities : cycle.Mold?.Cavities ?? 0;
}
