using FluentAssertions;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class OperatorWorkIntervalPolicyTests
{
    [Theory]
    [InlineData("2026-11-08T06:00:00Z", null, "2026-11-08T08:00:00Z", "2026-11-08T16:00:00Z", false)]
    [InlineData("2026-11-08T10:00:00Z", "2026-11-08T11:00:00Z", "2026-11-08T10:00:00Z", "2026-11-08T11:00:00Z", false)]
    [InlineData("2026-11-08T10:00:00Z", "2026-11-08T10:00:00Z", "2026-11-08T10:00:00Z", "2026-11-08T10:00:00Z", true)]
    [InlineData("2026-11-08T06:00:00Z", "2026-11-08T07:00:00Z", "2026-11-08T08:00:00Z", "2026-11-08T07:00:00Z", true)]
    [InlineData("2026-11-08T17:00:00Z", null, "2026-11-08T17:00:00Z", "2026-11-08T16:00:00Z", true)]
    public void Resolve_clamps_assignment_to_shift_and_marks_end_at_start_empty(
        string assigned, string? unassigned, string expectedStart, string expectedEnd, bool empty)
    {
        var result = OperatorWorkIntervalPolicy.Resolve(
            Utc(assigned), unassigned == null ? null : Utc(unassigned),
            Utc("2026-11-08T08:00:00Z"), Utc("2026-11-08T16:00:00Z"));

        result.WorkStart.Should().Be(Utc(expectedStart));
        result.WorkEnd.Should().Be(Utc(expectedEnd));
        result.IsEmpty.Should().Be(empty);
    }

    [Fact]
    public void Resolve_uses_actual_bounds_and_marks_pre_start_cancel_empty()
    {
        var result = OperatorWorkIntervalPolicy.Resolve(
            Utc("2026-11-18T08:00:00Z"), Utc("2026-11-18T08:00:00Z"),
            Utc("2026-11-18T08:00:00Z"), Utc("2026-11-18T20:00:00Z"));

        result.WorkStart.Should().Be(result.WorkEnd);
        result.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("2026-11-18T07:00:00Z", "2026-11-18T08:00:00Z")]
    [InlineData("2026-11-18T12:00:00Z", "2026-11-18T12:00:00Z")]
    [InlineData("2026-11-18T21:00:00Z", "2026-11-18T20:00:00Z")]
    public void DefaultUnassignedAt_is_clamped_to_actual_work_and_shift_end(
        string now, string expected)
    {
        OperatorWorkIntervalPolicy.DefaultUnassignedAt(
            Utc(now), Utc("2026-11-18T08:00:00Z"), Utc("2026-11-18T20:00:00Z"))
            .Should().Be(Utc(expected));
    }

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
