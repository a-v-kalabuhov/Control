using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class TaskPeriodPlanTests
{
    private static readonly DateTime S = new(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime H(double hours) => S.AddHours(hours);

    // Эталон 60 с, 2 гнезда → 120 шт/ч
    private static int Plan(Interval? release, int plan, DateTime from, DateTime to) =>
        TaskPeriodPlan.Calculate(release, plan, cavities: 2, fullCycleSeconds: 60, from, to);

    [Fact]
    public void TaskInsidePeriod_FullNormCappedByPlan()
    {
        Plan(new Interval(H(1), H(3)), 1000, H(0), H(24)).Should().Be(240);
        Plan(new Interval(H(1), H(3)), 100, H(0), H(24)).Should().Be(100);
    }

    [Fact]
    public void ThreeDayTask_SumOfDaysEqualsTotalNorm()
    {
        var release = new Interval(H(3), H(51)); // 48 ч → 5760 шт по норме
        var d1 = Plan(release, 100_000, H(-8), H(16));
        var d2 = Plan(release, 100_000, H(16), H(40));
        var d3 = Plan(release, 100_000, H(40), H(64));
        d2.Should().Be(2880);
        (d1 + d2 + d3).Should().Be(5760);
    }

    [Fact]
    public void SumOfDaysNeverExceedsPlan_TailGoesToDayWhereNormReachedPlan()
    {
        var release = new Interval(H(0), H(48));
        var d1 = Plan(release, 3000, H(0), H(24));  // норма 2880
        var d2 = Plan(release, 3000, H(24), H(48)); // остаток 120
        d1.Should().Be(2880);
        d2.Should().Be(120);
    }

    [Fact]
    public void PartialShot_RoundsDown()
    {
        // 90 с выпуска при эталоне 60 с → 1 смыкание × 2 гнезда
        Plan(new Interval(S, S.AddSeconds(90)), 1000, H(-1), H(1)).Should().Be(2);
    }

    [Fact]
    public void PeriodBeforeRelease_Zero()
    {
        Plan(new Interval(H(10), H(20)), 1000, H(0), H(5)).Should().Be(0);
    }

    [Fact]
    public void NoRelease_Zero()
    {
        Plan(null, 1000, H(0), H(24)).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void NoCycleNorm_Zero(int? cycle)
    {
        TaskPeriodPlan.Calculate(new Interval(H(0), H(5)), 1000, 2, cycle, H(0), H(24)).Should().Be(0);
    }

    [Fact]
    public void NoCavities_Zero()
    {
        TaskPeriodPlan.Calculate(new Interval(H(0), H(5)), 1000, 0, 60, H(0), H(24)).Should().Be(0);
    }

    [Fact]
    public void HugeValues_DoNotOverflow()
    {
        TaskPeriodPlan.Calculate(new Interval(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)),
            int.MaxValue, int.MaxValue, 1, H(0), H(24)).Should().BeInRange(0, int.MaxValue);
    }
}
