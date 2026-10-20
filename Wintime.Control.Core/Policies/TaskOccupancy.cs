using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Policies;

/// <summary>Вид отрезка занятости: план, факт или прогноз (PZP-07b).</summary>
public enum OccupancyKind { Plan, Fact, Forecast }

/// <summary>Отрезок занятости ТПА/ПФ заданием, UTC, полуоткрытый <c>[Start, End)</c>.</summary>
public sealed record OccupancySegment(OccupancyKind Kind, DateTime Start, DateTime End);

/// <summary>Статус задания и фактическое начало наладки — всё, что нужно отрезкам кроме плана и прогноза.</summary>
public sealed record OccupancyFacts(TaskStatus Status, DateTime? SetupStartedAt);

/// <summary>
/// Отрезки занятости задания для детекции конфликтов (PZP-07b, ADR-0022). Чистая функция, не хранится.
/// Planned и Issued до планового начала — план <c>[PS, PE)</c> с буфером; дальше — факт <c>[SetupStartedAt, now)</c>
/// и прогноз конца выпуска без буфера по стадии: Б — не начатое просроченное, В/Г — наладка в нормативе / сверх,
/// Д/Е — выпуск (<see cref="ReleaseForecast"/>). При опоздании считаем, что наладка начнётся/завершится через
/// <see cref="ExpectedDelay"/>. Completed/Closed ничего не занимают.
/// </summary>
public static class TaskOccupancy
{
    public static readonly TimeSpan ExpectedDelay = TimeSpan.FromMinutes(10);

    public static IReadOnlyList<OccupancySegment> Build(DateTime plannedStartUtc, TaskPlan plan, OccupancyFacts facts,
                                                        ReleaseForecastResult? forecast, DateTime nowUtc)
    {
        var setup = TimeSpan.FromSeconds(plan.SetupSeconds);
        var release = TimeSpan.FromSeconds(plan.ReleaseSeconds ?? 0);
        var planEnd = plan.PlannedEndAt ?? Add(plannedStartUtc, setup);
        var segments = new List<OccupancySegment>(2);

        if (facts.Status == TaskStatus.Planned
            || (facts.Status == TaskStatus.Issued && nowUtc <= plannedStartUtc))
        {
            Push(segments, OccupancyKind.Plan, plannedStartUtc, planEnd);                     // Planned, А
        }
        else if (facts.Status == TaskStatus.Issued)
        {
            var start = Add(nowUtc, ExpectedDelay);                                           // Б
            Push(segments, OccupancyKind.Forecast, start, Add(Add(start, setup), release));
        }
        else if (facts.Status == TaskStatus.Setup && facts.SetupStartedAt is DateTime setupStarted)
        {
            Push(segments, OccupancyKind.Fact, setupStarted, nowUtc);
            var releaseEnd = nowUtc - setupStarted <= setup
                ? Add(Add(setupStarted, setup), release)                                      // В
                : Add(Add(nowUtc, ExpectedDelay), release);                                   // Г
            Push(segments, OccupancyKind.Forecast, nowUtc, releaseEnd);
        }
        else if (facts.Status == TaskStatus.InProgress && facts.SetupStartedAt is DateTime started)
        {
            Push(segments, OccupancyKind.Fact, started, nowUtc);
            if (forecast?.ForecastAt is DateTime forecastAt)                                  // Д, Е
                Push(segments, OccupancyKind.Forecast, nowUtc, forecastAt);
        }

        return segments;
    }

    private static void Push(List<OccupancySegment> segments, OccupancyKind kind, DateTime start, DateTime end)
    {
        if (end > start)
            segments.Add(new OccupancySegment(kind, start, end));
    }

    // Защита от выхода за DateTime.MaxValue на абсурдных планах.
    private static DateTime Add(DateTime at, TimeSpan span) =>
        span >= DateTime.MaxValue - at ? DateTime.MaxValue : at + span;
}
