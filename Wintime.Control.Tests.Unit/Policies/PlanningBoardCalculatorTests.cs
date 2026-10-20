using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class PlanningBoardCalculatorTests
{
    // План: начало 10:00, наладка 30 мин, выпуск 60 мин, буфер 10 мин → окончание 11:40
    private static readonly DateTime Start = new(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TaskPlan Plan = new(1800, 3600, 600, Start.AddMinutes(100));
    private static DateTime At(int minutes) => Start.AddMinutes(minutes);

    private static BoardTaskFacts Facts(TaskStatus status, DateTime? setupStarted = null, DateTime? started = null,
                                        DateTime? completed = null, DateTime? closed = null) =>
        new(status, setupStarted, started, completed, closed);

    [Fact]
    public void Plan_segments_follow_setup_release_buffer()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Planned), null, At(-60));

        r.Plan.Should().Be(new BoardPlan(Start, At(30), At(90), At(100)));
        r.Fact.Should().Be(new BoardFact(null, null, null, null));
        r.BufferRemainingSeconds.Should().BeNull();
        r.OverdueNotStartedSeconds.Should().BeNull();
        r.SetupLagSeconds.Should().BeNull();
    }

    [Fact]
    public void No_release_plan_leaves_release_and_buffer_open()
    {
        var plan = new TaskPlan(1800, null, null, null);

        var r = PlanningBoardCalculator.Build(Start, plan, Facts(TaskStatus.Planned), null, At(-60));

        r.Plan.Should().Be(new BoardPlan(Start, At(30), null, null));
    }

    [Theory]
    [InlineData(TaskStatus.Planned)]
    [InlineData(TaskStatus.Issued)]
    public void Not_started_after_planned_start_is_overdue(TaskStatus status)
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(status), null, At(130));

        r.OverdueNotStartedSeconds.Should().Be(130 * 60);
    }

    [Fact]
    public void Setup_past_norm_lags_and_forecasts_release_from_now_plus_delay()
    {
        // наладка с 10:10, сейчас 10:45 (35 > 30) → прогноз 10:45 + 10 + 60 = 11:55
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Setup, setupStarted: At(10)),
            new ReleaseForecastResult(100m, At(200)), At(45));

        r.SetupLagSeconds.Should().Be(15 * 60);
        r.Fact.Should().Be(new BoardFact(At(10), null, null, At(115)));
        r.BufferRemainingSeconds.Should().BeNull();
        r.OverdueNotStartedSeconds.Should().BeNull();
    }

    [Fact]
    public void Setup_within_norm_forecasts_release_from_actual_setup_start()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Setup, setupStarted: At(0)), null, At(20));

        r.Fact.Should().Be(new BoardFact(At(0), null, null, At(90)));
    }

    [Fact]
    public void Issued_overdue_forecast_starts_after_expected_delay()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Issued), null, At(20));

        r.Fact.Should().Be(new BoardFact(null, null, null, At(120), At(30)));
        r.OverdueNotStartedSeconds.Should().Be(20 * 60);
    }

    [Fact]
    public void Issued_before_start_and_planned_have_no_forecast()
    {
        PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Issued), null, At(-10)).Fact
            .Should().Be(new BoardFact(null, null, null, null));
        PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Planned), null, At(20)).Fact
            .Should().Be(new BoardFact(null, null, null, null));
    }

    [Fact]
    public void Setup_within_norm_has_no_lag()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan, Facts(TaskStatus.Setup, setupStarted: At(0)), null, At(20));

        r.SetupLagSeconds.Should().BeNull();
    }

    [Fact]
    public void InProgress_forecast_before_plan_end_leaves_remaining_buffer()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.InProgress, setupStarted: At(5), started: At(32)),
            new ReleaseForecastResult(100m, At(95)), At(60));

        r.Fact.Should().Be(new BoardFact(At(5), At(32), null, At(95)));
        r.BufferRemainingSeconds.Should().Be(5 * 60);
    }

    [Fact]
    public void InProgress_forecast_after_plan_end_is_overrun()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.InProgress, setupStarted: At(5), started: At(32)),
            new ReleaseForecastResult(100m, At(110)), At(60));

        r.BufferRemainingSeconds.Should().Be(-10 * 60);
    }

    [Fact]
    public void InProgress_with_plan_met_finishes_now()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.InProgress, setupStarted: At(0), started: At(30)),
            new ReleaseForecastResult(100m, null), At(80));

        r.Fact.ForecastEnd.Should().Be(At(80));
        r.BufferRemainingSeconds.Should().Be(20 * 60);
    }

    [Fact]
    public void InProgress_without_forecast_has_no_remaining()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.InProgress, setupStarted: At(0), started: At(30)),
            new ReleaseForecastResult(null, null), At(80));

        r.Fact.ForecastEnd.Should().BeNull();
        r.BufferRemainingSeconds.Should().BeNull();
    }

    [Fact]
    public void Completed_remaining_is_from_completion()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.Completed, setupStarted: At(0), started: At(30), completed: At(85)), null, At(200));

        r.Fact.Should().Be(new BoardFact(At(0), At(30), At(85), null));
        r.BufferRemainingSeconds.Should().Be(15 * 60);
    }

    [Fact]
    public void Closed_after_release_without_completion_uses_closed_at()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.Closed, setupStarted: At(0), started: At(30), closed: At(120)), null, At(200));

        r.Fact.ReleaseEnd.Should().Be(At(120));
        r.BufferRemainingSeconds.Should().Be(-20 * 60);
    }

    [Fact]
    public void Closed_during_setup_ends_setup_at_close_and_has_no_release()
    {
        var r = PlanningBoardCalculator.Build(Start, Plan,
            Facts(TaskStatus.Closed, setupStarted: At(0), closed: At(20)), null, At(200));

        r.Fact.Should().Be(new BoardFact(At(0), At(20), null, null));
        r.BufferRemainingSeconds.Should().BeNull();
    }

    [Theory]
    [InlineData(-120, -60, false)]  // целиком до окна
    [InlineData(-60, 30, true)]     // начинается до окна, заканчивается внутри
    [InlineData(30, 90, true)]      // внутри
    [InlineData(300, 400, false)]   // после окна
    [InlineData(0, 0, true)]        // нулевой план на левой границе
    [InlineData(240, 240, false)]   // нулевой план на правой границе (конец не включён)
    public void PlanOverlaps_window(int startMin, int endMin, bool expected)
    {
        var plan = new BoardPlan(At(startMin), At(startMin), null, endMin == startMin ? null : At(endMin));

        PlanningBoardCalculator.PlanOverlaps(plan, At(0), At(240)).Should().Be(expected);
    }
}
