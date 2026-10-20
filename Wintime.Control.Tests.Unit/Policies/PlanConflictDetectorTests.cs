using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class PlanConflictDetectorTests
{
    private static readonly DateTime T0 = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(20);
    private static readonly Guid ImmA = Guid.NewGuid(), ImmB = Guid.NewGuid();
    private static readonly Guid MoldX = Guid.NewGuid(), MoldY = Guid.NewGuid();

    private static OccupancySegment S(OccupancyKind kind, int from, int to) => new(kind, T0.AddMinutes(from), T0.AddMinutes(to));
    private static ConflictTaskInput Task(Guid imm, Guid mold, params OccupancySegment[] segs) => new(Guid.NewGuid(), imm, mold, segs);

    [Fact]
    public void Plan_vs_plan_on_same_imm_over_threshold_is_plan_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, S(OccupancyKind.Plan, 60, 160));

        var r = PlanConflictDetector.Detect(new[] { b, a }, Threshold);

        r.Should().ContainSingle().Which.Should().Be(new PlanConflict(a.TaskId, b.TaskId, ConflictResource.Imm,
            ConflictKind.Plan, T0.AddMinutes(60), T0.AddMinutes(100), 40 * 60));
    }

    [Fact]
    public void Overlap_just_over_threshold_reports_seconds_above_threshold()
    {
        // 20 мин + 0,5 с: округление вниз показало бы ровно 1200 с, т.е. «не больше порога»
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, new OccupancySegment(OccupancyKind.Plan, T0.AddMinutes(80).AddMilliseconds(-500), T0.AddMinutes(160)));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Single().OverlapSeconds.Should().Be(20 * 60 + 1);
    }

    [Fact]
    public void Overlap_equal_to_threshold_is_not_a_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, S(OccupancyKind.Plan, 80, 160));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Should().BeEmpty();
    }

    [Fact]
    public void Overlap_one_second_over_threshold_is_a_conflict()
    {
        var a = Task(ImmA, MoldX, new OccupancySegment(OccupancyKind.Plan, T0, T0.AddMinutes(100)));
        var b = Task(ImmA, MoldY, new OccupancySegment(OccupancyKind.Plan, T0.AddMinutes(80).AddSeconds(-1), T0.AddMinutes(160)));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Should().ContainSingle();
    }

    [Fact]
    public void Touching_segments_are_not_a_conflict_even_with_zero_threshold()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldY, S(OccupancyKind.Plan, 100, 160));

        PlanConflictDetector.Detect(new[] { a, b }, TimeSpan.Zero).Should().BeEmpty();
    }

    [Theory]
    [InlineData(OccupancyKind.Fact)]
    [InlineData(OccupancyKind.Forecast)]
    public void Any_fact_or_forecast_in_pair_makes_deviation_conflict(OccupancyKind kind)
    {
        var running = Task(ImmA, MoldX, S(OccupancyKind.Fact, 0, 50), S(OccupancyKind.Forecast, 50, 120));
        var planned = Task(ImmA, MoldY, S(OccupancyKind.Plan, kind == OccupancyKind.Fact ? 10 : 70, 200));

        PlanConflictDetector.Detect(new[] { running, planned }, Threshold)
            .Should().ContainSingle().Which.Kind.Should().Be(ConflictKind.Deviation);
    }

    [Fact]
    public void Overlap_is_summed_over_all_segment_pairs()
    {
        // факт 0..50 и прогноз 50..70 против плана 40..200 → 10 + 20 = 30 мин > 20
        var running = Task(ImmA, MoldX, S(OccupancyKind.Fact, 0, 50), S(OccupancyKind.Forecast, 50, 70));
        var planned = Task(ImmA, MoldY, S(OccupancyKind.Plan, 40, 200));

        var c = PlanConflictDetector.Detect(new[] { running, planned }, Threshold).Single();

        c.OverlapSeconds.Should().Be(30 * 60);
        c.OverlapStart.Should().Be(T0.AddMinutes(40));
        c.OverlapEnd.Should().Be(T0.AddMinutes(70));
    }

    [Fact]
    public void Same_mold_on_different_imms_is_mold_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmB, MoldX, S(OccupancyKind.Plan, 50, 150));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold)
            .Should().ContainSingle().Which.Resource.Should().Be(ConflictResource.Mold);
    }

    [Fact]
    public void Same_imm_and_same_mold_gives_two_records()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmA, MoldX, S(OccupancyKind.Plan, 50, 150));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold)
            .Select(c => c.Resource).Should().BeEquivalentTo(new[] { ConflictResource.Imm, ConflictResource.Mold });
    }

    [Fact]
    public void Different_imm_and_mold_never_conflict()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var b = Task(ImmB, MoldY, S(OccupancyKind.Plan, 0, 100));

        PlanConflictDetector.Detect(new[] { a, b }, Threshold).Should().BeEmpty();
    }

    [Fact]
    public void Task_without_segments_is_ignored()
    {
        var a = Task(ImmA, MoldX, S(OccupancyKind.Plan, 0, 100));
        var finished = Task(ImmA, MoldX);

        PlanConflictDetector.Detect(new[] { a, finished }, Threshold).Should().BeEmpty();
    }

    [Fact]
    public void Plan_conflicts_come_before_deviation_conflicts()
    {
        var running = Task(ImmA, MoldX, S(OccupancyKind.Fact, 0, 50));
        var p1 = Task(ImmA, MoldY, S(OccupancyKind.Plan, 10, 200));
        var p2 = Task(ImmB, Guid.NewGuid(), S(OccupancyKind.Plan, 300, 400));
        var p3 = Task(ImmB, Guid.NewGuid(), S(OccupancyKind.Plan, 350, 450));

        PlanConflictDetector.Detect(new[] { running, p1, p2, p3 }, Threshold)
            .Select(c => c.Kind).Should().Equal(ConflictKind.Plan, ConflictKind.Deviation);
    }
}
