using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Policies;

/// <summary>Плановые границы бара задания, UTC: наладка → выпуск → буфер. <c>null</c> — выпуск не посчитать.</summary>
public sealed record BoardPlan(DateTime SetupStart, DateTime SetupEnd, DateTime? ReleaseEnd, DateTime? BufferEnd);

/// <summary>Фактические границы бара, UTC; <c>ForecastEnd</c> — прогноз окончания выпуска (только InProgress).</summary>
public sealed record BoardFact(DateTime? SetupStart, DateTime? SetupEnd, DateTime? ReleaseEnd, DateTime? ForecastEnd);

/// <summary>Статус и метки задания, нужные бару.</summary>
public sealed record BoardTaskFacts(TaskStatus Status, DateTime? SetupStartedAt, DateTime? StartedAt,
                                    DateTime? CompletedAt, DateTime? ClosedAt);

/// <summary>
/// Бар задания на Гантте. <c>BufferRemainingSeconds</c> &lt; 0 — перерасход сверх буфера;
/// <c>OverdueNotStartedSeconds</c> — просрочка неначатого; <c>SetupLagSeconds</c> — отставание наладки.
/// </summary>
public sealed record BoardTiming(BoardPlan Plan, BoardFact Fact, long? BufferRemainingSeconds,
                                 long? OverdueNotStartedSeconds, long? SetupLagSeconds);

/// <summary>
/// План-график цеха (PZP-07a): план и факт задания для пары баров. Чистая функция, не хранится.
/// Остаток буфера = плановое окончание (с буфером) − окончание выпуска: прогноз для InProgress,
/// факт для завершённых. Для Setup прогноз и остаток не считаются — только отставание наладки.
/// </summary>
public static class PlanningBoardCalculator
{
    public static BoardTiming Build(DateTime plannedStartUtc, TaskPlan plan, BoardTaskFacts facts,
                                    ReleaseForecastResult? forecast, DateTime nowUtc)
    {
        var setupEnd = plannedStartUtc.AddSeconds(plan.SetupSeconds);
        DateTime? bufferEnd = plan.PlannedEndAt;
        DateTime? releaseEnd = bufferEnd is DateTime b && plan.BufferSeconds is long buffer
            ? b.AddSeconds(-buffer)
            : null;
        var planBar = new BoardPlan(plannedStartUtc, setupEnd, releaseEnd, bufferEnd);

        var finishedAt = facts.CompletedAt ?? facts.ClosedAt;
        // Закрыто во время наладки — наладка кончилась закрытием, выпуска не было.
        DateTime? factSetupEnd = facts.StartedAt ?? (facts.SetupStartedAt != null ? finishedAt : null);
        DateTime? factReleaseEnd = facts.StartedAt != null ? finishedAt : null;
        DateTime? forecastEnd = facts.Status == TaskStatus.InProgress ? ForecastEnd(forecast, nowUtc) : null;
        var factBar = new BoardFact(facts.SetupStartedAt, factSetupEnd, factReleaseEnd, forecastEnd);

        DateTime? releaseFinish = facts.Status == TaskStatus.InProgress ? forecastEnd
            : facts.Status is TaskStatus.Completed or TaskStatus.Closed ? factReleaseEnd
            : null;
        long? remaining = bufferEnd is DateTime p && releaseFinish is DateTime e ? Seconds(p - e) : null;

        long? overdue = facts.Status is TaskStatus.Planned or TaskStatus.Issued && nowUtc > plannedStartUtc
            ? Seconds(nowUtc - plannedStartUtc)
            : null;
        long? lag = facts.Status == TaskStatus.Setup && nowUtc > setupEnd ? Seconds(nowUtc - setupEnd) : null;

        return new BoardTiming(planBar, factBar, remaining, overdue, lag);
    }

    /// <summary>
    /// Плановое окно <c>[SetupStart, BufferEnd ?? SetupEnd)</c> пересекает <c>[fromUtc, toUtc)</c>.
    /// Окно нулевой длины — точка: попадает, если лежит в <c>[fromUtc, toUtc)</c>.
    /// </summary>
    public static bool PlanOverlaps(BoardPlan plan, DateTime fromUtc, DateTime toUtc)
    {
        var end = plan.BufferEnd ?? plan.SetupEnd;
        return plan.SetupStart < toUtc && (end > fromUtc || plan.SetupStart >= fromUtc);
    }

    // План выполнен (темп есть, прогноза нет) — выпуск кончается «сейчас»; темпа нет — не посчитать.
    private static DateTime? ForecastEnd(ReleaseForecastResult? forecast, DateTime nowUtc) =>
        forecast is null ? null : forecast.ForecastAt ?? (forecast.RatePerHour != null ? nowUtc : null);

    private static long Seconds(TimeSpan span) => (long)Math.Floor(span.TotalSeconds);
}
