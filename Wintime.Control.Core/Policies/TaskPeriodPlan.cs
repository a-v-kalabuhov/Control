namespace Wintime.Control.Core.Policies;

/// <summary>
/// Нормативный план задания за период (ARCH-03, ADR-0016): сколько штук должно было выйти
/// за время выпуска внутри <c>[from, to)</c> по эталону полного цикла.
/// <c>N(t) = ⌊(min(t, E) − S) / C⌋ × K</c> — накопленный норматив к моменту t;
/// план за период = <c>min(P, N(to)) − min(P, N(from))</c>. Поэтому сумма по дням равна плану
/// задания (не больше P), а простои внутри выпуска не вычитаются — это потери, видимые как
/// разрыв план/факт. Гнёзда K — текущие <c>Mold.Cavities</c> (прогноз, как в TaskPlanCalculator).
/// </summary>
public static class TaskPeriodPlan
{
    /// <param name="release">Окно выпуска (<see cref="TaskActivityWindow.Release"/>); <c>null</c> → 0.</param>
    public static int Calculate(Interval? release, int planQuantity, int cavities, int? fullCycleSeconds,
                                DateTime fromUtc, DateTime toUtc)
    {
        if (release is null || fullCycleSeconds is not > 0 || cavities <= 0 || planQuantity <= 0)
            return 0;

        long cap = planQuantity;
        long Cumulative(DateTime t)
        {
            var until = t < release.End ? t : release.End;
            if (until <= release.Start)
                return 0;
            long shots = (long)((until - release.Start).TotalSeconds / fullCycleSeconds.Value);
            // shots × K с насыщением до плана: переполнения быть не должно
            return shots >= (cap + cavities - 1) / cavities ? cap : Math.Min(cap, shots * cavities);
        }

        return (int)(Cumulative(toUtc) - Cumulative(fromUtc));
    }
}
