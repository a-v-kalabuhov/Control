namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правило смежности эпизода «работы без задания» и задания (PZP-04, UC-5).
/// Задание допустимо в кандидаты, если у него нет разрыва во времени с эпизодом
/// (пересечение / сразу после / сразу перед) либо это задание без циклов в ту же дату.
/// Порог разрыва — 1.5 × средней длительности цикла эпизода, симметрично с обеих сторон.
/// </summary>
public static class UnplannedRunAdjacency
{
    public const double ToleranceFactor = 1.5;

    /// <summary>Рабочий интервал задания пересекается с окном эпизода.</summary>
    public static bool Overlaps(DateTime taskStart, DateTime taskEnd, DateTime episodeStart, DateTime episodeEnd)
        => taskStart < episodeEnd && taskEnd > episodeStart;

    /// <summary>Задание началось сразу после эпизода (разрыв справа меньше порога).</summary>
    public static bool AdjacentAfter(DateTime taskFirstActivity, DateTime episodeEnd, double avgCycleSeconds)
        => taskFirstActivity >= episodeEnd
        && (taskFirstActivity - episodeEnd).TotalSeconds < ToleranceFactor * avgCycleSeconds;

    /// <summary>Задание закончилось сразу перед эпизодом (разрыв слева меньше порога).</summary>
    public static bool AdjacentBefore(DateTime taskLastCycleEnd, DateTime episodeStart, double avgCycleSeconds)
        => taskLastCycleEnd <= episodeStart
        && (episodeStart - taskLastCycleEnd).TotalSeconds < ToleranceFactor * avgCycleSeconds;

    /// <summary>
    /// Плановая дата задания совпадает с датой начала эпизода по часам завода (BL-38):
    /// эпизод ночной смены после местной полуночи относится к новым суткам завода, а не к UTC-дате.
    /// </summary>
    public static bool SameDate(DateOnly plannedDate, DateTime episodeStartUtc, TimeZoneInfo factoryTz)
        => plannedDate == DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(episodeStartUtc, DateTimeKind.Utc), factoryTz));
}
