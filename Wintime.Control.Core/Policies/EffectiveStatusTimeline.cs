using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

public record RawSegment(string Status, DateTime Start, DateTime End);
public record TaskInterval(ActiveTaskStatus Status, DateTime Start, DateTime End);
public record Interval(DateTime Start, DateTime End);
public record EffectiveSegment(string EffectiveStatus, DateTime Start, DateTime End);

/// <summary>
/// Реконструкция таймлайна эффективного состояния за период [from, to] наложением трёх
/// историзированных рядов: сырой статус (ImmStatusHistory), интервалы статуса задания и
/// интервалы простоев (Event). Для истории thresholdPassed не нужен — факт простоя уже
/// материализован в Event, поэтому подаётся false.
/// </summary>
public static class EffectiveStatusTimeline
{
    public static IReadOnlyList<EffectiveSegment> Build(
        IReadOnlyList<RawSegment> raw,
        IReadOnlyList<TaskInterval> tasks,
        IReadOnlyList<Interval> downtimes,
        DateTime from, DateTime to)
    {
        if (to <= from) return System.Array.Empty<EffectiveSegment>();

        // 1. Собрать все границы внутри окна.
        var points = new SortedSet<DateTime> { from, to };
        void AddBound(DateTime d) { if (d > from && d < to) points.Add(d); }
        foreach (var s in raw)       { AddBound(s.Start); AddBound(s.End); }
        foreach (var t in tasks)     { AddBound(t.Start); AddBound(t.End); }
        foreach (var d in downtimes) { AddBound(d.Start); AddBound(d.End); }

        var bounds = points.ToList();

        // 2. На каждом под-интервале вычислить эффективное состояние в его середине.
        var segments = new List<EffectiveSegment>();
        for (int i = 0; i < bounds.Count - 1; i++)
        {
            var start = bounds[i];
            var end = bounds[i + 1];
            var mid = start + (end - start) / 2;

            var rawMode = raw.FirstOrDefault(s => s.Start <= mid && mid < s.End)?.Status
                          ?? ImmStatus.Offline;
            var task = tasks.FirstOrDefault(t => t.Start <= mid && mid < t.End)?.Status
                       ?? ActiveTaskStatus.None;
            var hasDowntime = downtimes.Any(d => d.Start <= mid && mid < d.End);

            var eff = ImmEffectiveStatus.Resolve(rawMode, task, hasDowntime, thresholdPassed: false);

            // 3. Слить со смежным сегментом, если состояние то же.
            if (segments.Count > 0 && segments[^1].EffectiveStatus == eff)
                segments[^1] = segments[^1] with { End = end };
            else
                segments.Add(new EffectiveSegment(eff, start, end));
        }

        return segments;
    }
}
