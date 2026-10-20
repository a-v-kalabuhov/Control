using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskOccupancyTests
{
    // План: начало 10:00, наладка 30 мин, выпуск 60 мин, буфер 10 мин → окончание 11:40
    private static readonly DateTime Start = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TaskPlan Plan = new(1800, 3600, 600, Start.AddMinutes(100));
    private static DateTime At(int minutes) => Start.AddMinutes(minutes);

    private static IReadOnlyList<OccupancySegment> Build(TaskStatus status, DateTime now, DateTime? setupStarted = null,
                                                         ReleaseForecastResult? forecast = null) =>
        TaskOccupancy.Build(Start, Plan, new OccupancyFacts(status, setupStarted), forecast, now);

    private static OccupancySegment Seg(OccupancyKind kind, int from, int to) => new(kind, At(from), At(to));

    [Fact]
    public void Planned_is_plan_window_with_buffer_even_when_overdue()
    {
        Build(TaskStatus.Planned, At(300)).Should().Equal(Seg(OccupancyKind.Plan, 0, 100));
    }

    [Fact]
    public void Issued_before_planned_start_is_plan_window_case_A()
    {
        Build(TaskStatus.Issued, At(-60)).Should().Equal(Seg(OccupancyKind.Plan, 0, 100));
    }

    [Fact]
    public void Issued_exactly_at_planned_start_is_still_plan_window()
    {
        Build(TaskStatus.Issued, At(0)).Should().Equal(Seg(OccupancyKind.Plan, 0, 100));
    }

    [Fact]
    public void Issued_after_planned_start_forecasts_from_now_plus_delay_without_buffer_case_B()
    {
        // now 10:20 → старт наладки 10:30, наладка 30 + выпуск 60 → 12:00
        Build(TaskStatus.Issued, At(20)).Should().Equal(Seg(OccupancyKind.Forecast, 30, 120));
    }

    [Fact]
    public void Setup_within_norm_forecasts_from_actual_setup_start_case_V()
    {
        // наладка с 10:05, сейчас 10:25 (20 ≤ 30) → выпуск кончится 10:05 + 30 + 60 = 11:35
        Build(TaskStatus.Setup, At(25), setupStarted: At(5)).Should().Equal(
            Seg(OccupancyKind.Fact, 5, 25), Seg(OccupancyKind.Forecast, 25, 95));
    }

    [Fact]
    public void Setup_exactly_at_norm_is_case_V()
    {
        Build(TaskStatus.Setup, At(35), setupStarted: At(5)).Should().Equal(
            Seg(OccupancyKind.Fact, 5, 35), Seg(OccupancyKind.Forecast, 35, 95));
    }

    [Fact]
    public void Setup_over_norm_forecasts_now_plus_delay_plus_release_case_G()
    {
        // наладка с 10:05, сейчас 10:50 (45 > 30) → 10:50 + 10 + 60 = 12:00
        Build(TaskStatus.Setup, At(50), setupStarted: At(5)).Should().Equal(
            Seg(OccupancyKind.Fact, 5, 50), Seg(OccupancyKind.Forecast, 50, 120));
    }

    [Fact]
    public void InProgress_uses_release_forecast_cases_D_E()
    {
        Build(TaskStatus.InProgress, At(60), setupStarted: At(-5), forecast: new ReleaseForecastResult(100m, At(130)))
            .Should().Equal(Seg(OccupancyKind.Fact, -5, 60), Seg(OccupancyKind.Forecast, 60, 130));
    }

    [Fact]
    public void InProgress_with_plan_met_has_only_fact()
    {
        Build(TaskStatus.InProgress, At(60), setupStarted: At(0), forecast: new ReleaseForecastResult(100m, null))
            .Should().Equal(Seg(OccupancyKind.Fact, 0, 60));
    }

    [Fact]
    public void Early_setup_start_moves_fact_left_of_plan()
    {
        Build(TaskStatus.Setup, At(-10), setupStarted: At(-40)).First().Should().Be(Seg(OccupancyKind.Fact, -40, -10));
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public void Finished_tasks_occupy_nothing(TaskStatus status)
    {
        Build(status, At(60), setupStarted: At(0)).Should().BeEmpty();
    }

    [Fact]
    public void No_release_plan_occupies_setup_only()
    {
        var plan = new TaskPlan(1800, null, null, null);
        TaskOccupancy.Build(Start, plan, new OccupancyFacts(TaskStatus.Planned, null), null, At(-60))
            .Should().Equal(Seg(OccupancyKind.Plan, 0, 30));
    }

    [Fact]
    public void Huge_release_plan_does_not_throw_and_is_clamped_to_max_date_case_B()
    {
        // ReleaseSeconds > диапазона TimeSpan — не должно падать OverflowException, отрезок упирается в DateTime.MaxValue
        var plan = new TaskPlan(1800, long.MaxValue / 2, 0, null);
        var result = TaskOccupancy.Build(Start, plan, new OccupancyFacts(TaskStatus.Issued, null), null, At(20));
        result.Should().ContainSingle().Which.Should().Be(new OccupancySegment(OccupancyKind.Forecast, At(30), DateTime.MaxValue));
        result[0].End.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Huge_release_plan_in_setup_does_not_throw_case_V()
    {
        var plan = new TaskPlan(1800, long.MaxValue / 2, 0, null);
        var result = TaskOccupancy.Build(Start, plan, new OccupancyFacts(TaskStatus.Setup, At(5)), null, At(25));
        result.Should().HaveCount(2);
        result[1].Should().Be(new OccupancySegment(OccupancyKind.Forecast, At(25), DateTime.MaxValue));
        result[1].End.Kind.Should().Be(DateTimeKind.Utc);
    }
}
