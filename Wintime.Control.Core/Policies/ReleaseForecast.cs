namespace Wintime.Control.Core.Policies;

/// <summary>
/// Темп (шт/ч) и прогноз окончания выпуска (UTC). <c>null</c> — не посчитать (нет гнёзд, нет
/// периода). <c>ForecastAt = null</c> также при выполненном плане или выходе за <see cref="DateTime.MaxValue"/>.
/// </summary>
public sealed record ReleaseForecastResult(decimal? RatePerHour, DateTime? ForecastAt);

/// <summary>
/// Прогноз выпуска по среднему полному периоду цикла (ADR-0017): период = средняя длительность
/// закрытых циклов задания + средняя пауза перед циклом (<c>null</c>-паузы — наладка, простои —
/// в среднее не входят). Прошлые остановки в будущее не экстраполируются: прогноз = «если дальше
/// работать без остановок». Пока закрытых циклов меньше <see cref="MinClosedCycles"/> — эталон
/// задания. Гнёзда — текущие <c>Mold.Cavities</c>: это прогноз, а не пересчёт истории.
/// Чистая функция; общая для плитки ТПА и Гантта (PZP-07).
/// </summary>
public static class ReleaseForecast
{
    public const int MinClosedCycles = 3;

    public static ReleaseForecastResult Calculate(DateTime nowUtc, int planQuantity, int actualQuantity,
                                                  int cavities, int closedCycles, double avgDurationSeconds,
                                                  double? avgPauseMs, int? plannedFullCycleSeconds)
    {
        double? period = closedCycles >= MinClosedCycles
            ? avgDurationSeconds + (avgPauseMs ?? 0) / 1000
            : plannedFullCycleSeconds;

        if (cavities <= 0 || period is not double p || !double.IsFinite(p) || p <= 0)
            return new ReleaseForecastResult(null, null);

        var rawRate = (double)cavities * 3600 / p;
        if (!double.IsFinite(rawRate) || rawRate > (double)decimal.MaxValue)
            return new ReleaseForecastResult(null, null);
        var rate = (decimal)rawRate;

        var remaining = planQuantity - actualQuantity;
        if (remaining <= 0)
            return new ReleaseForecastResult(rate, null);

        long shots = ((long)remaining + cavities - 1) / cavities;
        var seconds = shots * p;
        var maxSeconds = (DateTime.MaxValue - nowUtc).TotalSeconds;
        DateTime? forecast = seconds < maxSeconds ? nowUtc.AddSeconds(seconds) : null;

        return new ReleaseForecastResult(rate, forecast);
    }
}
