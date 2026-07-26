using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class UnplannedRunAdjacencyTests
{
    private static readonly DateTime E0 = new(2026, 7, 25, 10, 0, 0, DateTimeKind.Utc);   // episode start
    private static readonly DateTime E1 = new(2026, 7, 25, 10, 30, 0, DateTimeKind.Utc);  // episode end
    private const double Avg = 60; // сек

    [Fact]
    public void Overlaps_true_when_intervals_intersect()
        => UnplannedRunAdjacency.Overlaps(E0.AddMinutes(15), E1.AddMinutes(15), E0, E1).Should().BeTrue();

    [Fact]
    public void Overlaps_false_when_disjoint()
        => UnplannedRunAdjacency.Overlaps(E1.AddMinutes(5), E1.AddMinutes(20), E0, E1).Should().BeFalse();

    [Fact]
    public void AdjacentAfter_true_when_gap_below_threshold()
        => UnplannedRunAdjacency.AdjacentAfter(E1.AddSeconds(80), E1, Avg).Should().BeTrue(); // 80 < 1.5*60=90

    [Fact]
    public void AdjacentAfter_false_when_gap_at_or_above_threshold()
        => UnplannedRunAdjacency.AdjacentAfter(E1.AddSeconds(90), E1, Avg).Should().BeFalse();

    [Fact]
    public void AdjacentBefore_true_when_gap_below_threshold()
        => UnplannedRunAdjacency.AdjacentBefore(E0.AddSeconds(-80), E0, Avg).Should().BeTrue();

    [Fact]
    public void AdjacentBefore_false_when_gap_at_or_above_threshold()
        => UnplannedRunAdjacency.AdjacentBefore(E0.AddSeconds(-90), E0, Avg).Should().BeFalse();

    [Fact]
    public void SameDate_true_when_same_calendar_day_utc()
        => UnplannedRunAdjacency.SameDate(new DateTime(2026, 7, 25, 23, 59, 0, DateTimeKind.Utc), E0).Should().BeTrue();
}
