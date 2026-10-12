namespace Wintime.Control.Core.Policies;

/// <summary>
/// Плановые длительности задания (PZP-14), секунды. <c>null</c> — выпуск не посчитать
/// (нет эталона полного цикла или гнёзд), тогда неизвестны и буфер, и окончание.
/// </summary>
public sealed record TaskPlan(long SetupSeconds, long? ReleaseSeconds, long? BufferSeconds, DateTime? PlannedEndAt);

/// <summary>
/// План задания: наладка → выпуск → буфер выпуска. Чистая функция, результат не хранится.
/// Выпуск = ⌈план / гнёзда⌉ × эталон полного цикла; буфер = фикс. часть + процент от выпуска
/// (наладка в базу процента не входит), процентная часть округляется вверх до секунды.
/// Гнёзда — текущие <c>Mold.Cavities</c>: это прогноз, а не пересчёт истории.
/// </summary>
public static class TaskPlanCalculator
{
    public static TaskPlan Calculate(DateTime plannedStartUtc, int setupMinutes,
                                     int planQuantity, int cavities, int? fullCycleSeconds,
                                     int bufferFixedMinutes, int bufferPercent)
    {
        long setupSeconds = setupMinutes * 60L;

        if (fullCycleSeconds is not int cycle || cavities <= 0)
            return new TaskPlan(setupSeconds, null, null, null);

        long shots = ((long)planQuantity + cavities - 1) / cavities;
        long releaseSeconds = shots * cycle;
        long bufferSeconds = bufferFixedMinutes * 60L + (releaseSeconds * bufferPercent + 99) / 100;
        var end = plannedStartUtc.AddSeconds(setupSeconds + releaseSeconds + bufferSeconds);

        return new TaskPlan(setupSeconds, releaseSeconds, bufferSeconds, end);
    }
}
