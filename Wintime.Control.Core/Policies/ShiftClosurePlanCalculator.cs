using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

/// <summary>Нормативный план задания на смену и связанные с ним расчёты закрытия.</summary>
public static class ShiftClosurePlanCalculator
{
    public static int RemainingAtStart(int planQuantity, int previousOutput) =>
        Math.Max(0, planQuantity - previousOutput);

    public static int Calculate(ShiftTask task, DateTime plannedStartAtUtc,
        DateTime shiftStartUtc, DateTime shiftEndUtc, int previousOutput)
    {
        var remaining = RemainingAtStart(task.PlanQuantity, previousOutput);
        Interval? release;
        if (task.StartedAt.HasValue)
        {
            release = TaskActivityWindow.Release(task, shiftEndUtc);
            if (release != null && !task.CompletedAt.HasValue && !task.ClosedAt.HasValue)
                release = new Interval(release.Start, shiftEndUtc);
        }
        else
        {
            var start = plannedStartAtUtc.AddMinutes(task.PlannedSetupMinutes);
            var plan = TaskPlanCalculator.Calculate(plannedStartAtUtc,
                task.PlannedSetupMinutes, task.PlanQuantity, task.Cavities,
                task.PlannedFullCycleSeconds, 0, 0);
            release = plan.ReleaseSeconds is long seconds
                ? new Interval(start, start.AddSeconds(seconds))
                : null;
        }

        return Math.Min(remaining, TaskPeriodPlan.Calculate(release,
            task.PlanQuantity, task.Cavities, task.PlannedFullCycleSeconds,
            shiftStartUtc, shiftEndUtc));
    }

    public static decimal MaterialWeightGrams(int shiftPlanQuantity, int cavities,
        decimal partWeightGrams, decimal runnerWeightGrams)
    {
        if (shiftPlanQuantity <= 0 || cavities <= 0)
            return 0;

        var shots = Math.Ceiling(shiftPlanQuantity / (decimal)cavities);
        return shiftPlanQuantity * partWeightGrams + shots * runnerWeightGrams;
    }
}
