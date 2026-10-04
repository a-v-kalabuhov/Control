using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Infrastructure.Reports;

/// <summary>
/// Раскладка таймлайна эффективного состояния по локальным суткам завода (отчёт
/// «Производительность оборудования»). Непокрытое сегментами время суток — в т.ч. будущее
/// после «сейчас» — <see cref="EffectiveStatus.NoData"/>, поэтому сумма по суткам всегда
/// равна длине суток (24 ч; 23/25 ч в сутки перевода часов).
/// </summary>
internal static class DailyStatusBreakdown
{
    /// <summary>Категории столбца; порядок = порядок в стеке диаграммы и колонок Excel.</summary>
    internal static readonly string[] Keys =
    [
        EffectiveStatus.Production, EffectiveStatus.Setup, EffectiveStatus.Downtime,
        EffectiveStatus.Unplanned, EffectiveStatus.NoTask, EffectiveStatus.Offline, EffectiveStatus.NoData
    ];

    internal static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [EffectiveStatus.Production] = "Работа",
        [EffectiveStatus.Setup]      = "Наладка",
        [EffectiveStatus.Downtime]   = "Простой",
        [EffectiveStatus.Unplanned]  = "Работа без задания",
        [EffectiveStatus.NoTask]     = "Без задания",
        [EffectiveStatus.Offline]    = "Нет связи",
        [EffectiveStatus.NoData]     = "Нет данных",
    };

    internal static Dictionary<string, int> Empty() => Keys.ToDictionary(k => k, _ => 0);

    internal static List<Dictionary<string, int>> Split(
        IReadOnlyList<EffectiveSegment> segments, IReadOnlyList<FactoryDay> days)
    {
        var result = new List<Dictionary<string, int>>(days.Count);
        foreach (var day in days)
        {
            var secs = Empty();
            var covered = 0;
            foreach (var s in segments)
            {
                var start = s.Start > day.StartUtc ? s.Start : day.StartUtc;
                var end   = s.End   < day.EndUtc   ? s.End   : day.EndUtc;
                if (end <= start) continue;

                // Телескопически от начала суток: смежные срезы суммируются точно, доли секунды не теряются.
                var d = (int)Math.Floor((end - day.StartUtc).TotalSeconds)
                      - (int)Math.Floor((start - day.StartUtc).TotalSeconds);
                secs[s.EffectiveStatus] += d;
                covered += d;
            }

            // Остаток (не покрыт таймлайном) — «Нет данных».
            secs[EffectiveStatus.NoData] += (int)(day.EndUtc - day.StartUtc).TotalSeconds - covered;
            result.Add(secs);
        }
        return result;
    }

    /// <summary>Работа / (всё время − Нет связи − Нет данных) × 100; нет известного времени → null.</summary>
    internal static decimal? Efficiency(IReadOnlyDictionary<string, int> seconds) =>
        EfficiencyCore(
            seconds.Values.Sum(v => (long)v), seconds[EffectiveStatus.Production],
            seconds[EffectiveStatus.Offline], seconds[EffectiveStatus.NoData]);

    /// <summary>Перегрузка для сумм по парку (long): не переполняется при N ТПА × M суток.</summary>
    internal static decimal? Efficiency(IReadOnlyDictionary<string, long> seconds) =>
        EfficiencyCore(
            seconds.Values.Sum(), seconds[EffectiveStatus.Production],
            seconds[EffectiveStatus.Offline], seconds[EffectiveStatus.NoData]);

    private static decimal? EfficiencyCore(long all, long production, long offline, long noData)
    {
        var known = all - offline - noData;
        return known > 0 ? Math.Round((decimal)production / known * 100, 2) : null;
    }
}
