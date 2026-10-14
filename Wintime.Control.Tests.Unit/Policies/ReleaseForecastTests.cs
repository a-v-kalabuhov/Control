using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ReleaseForecastTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Calculate_EnoughCycles_UsesCyclePlusPause()
    {
        // период 20 + 10 = 30 с; темп 2 × 3600 / 30 = 240; остаток 60 шт → 30 смыканий × 30 с
        var r = ReleaseForecast.Calculate(Now, planQuantity: 100, actualQuantity: 40, cavities: 2,
            closedCycles: 3, avgDurationSeconds: 20, avgPauseMs: 10_000, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(240m);
        r.ForecastAt.Should().Be(Now.AddSeconds(900));
        r.ForecastAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_NoPauses_TreatsPauseAsZero()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 5,
            avgDurationSeconds: 20, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(360m);
        r.ForecastAt.Should().Be(Now.AddSeconds(600));
    }

    [Fact]
    public void Calculate_FewCycles_FallsBackToPlannedFullCycle()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 2,
            avgDurationSeconds: 20, avgPauseMs: 10_000, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(120m);
        r.ForecastAt.Should().Be(Now.AddSeconds(1800));
    }

    [Fact]
    public void Calculate_FewCyclesAndNoPlannedCycle_ReturnsNulls()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 0,
            avgDurationSeconds: 0, avgPauseMs: null, plannedFullCycleSeconds: null);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_PartialLastShot_RoundsShotsUp()
    {
        // 101 шт / 4 гнезда = 26 смыканий × 60 с
        var r = ReleaseForecast.Calculate(Now, 101, 0, 4, closedCycles: 3,
            avgDurationSeconds: 60, avgPauseMs: null, plannedFullCycleSeconds: null);

        r.ForecastAt.Should().Be(Now.AddSeconds(26 * 60));
    }

    [Fact]
    public void Calculate_PlanDone_KeepsRateHidesForecast()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 100, 2, closedCycles: 50,
            avgDurationSeconds: 20, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(360m);
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ZeroCavities_ReturnsNulls()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 0, closedCycles: 5,
            avgDurationSeconds: 20, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ZeroPeriod_ReturnsNulls()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 5,
            avgDurationSeconds: 0, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ForecastBeyondMaxDate_ReturnsNullForecast()
    {
        var r = ReleaseForecast.Calculate(DateTime.MaxValue.AddDays(-1), int.MaxValue, 0, 1, closedCycles: 5,
            avgDurationSeconds: 3600, avgPauseMs: null, plannedFullCycleSeconds: null);

        r.RatePerHour.Should().Be(1m);
        r.ForecastAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Calculate_NonPositiveFallbackPeriod_ReturnsNulls(int planned)
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 0,
            avgDurationSeconds: 0, avgPauseMs: null, plannedFullCycleSeconds: planned);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_NaNAverage_ReturnsNullsWithoutThrowing()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 5,
            avgDurationSeconds: double.NaN, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }
}
