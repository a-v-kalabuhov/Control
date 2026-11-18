using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftOfflineFallbackTests
{
    private static readonly DateTime ShiftStart = Utc("2026-11-18T08:00:00");
    private static readonly DateTime FactEnd = Utc("2026-11-18T10:00:00");
    private static readonly Interval Activity = new(ShiftStart, FactEnd);

    [Fact]
    public void Any_raw_offline_inside_activity_requires_fallback_only_with_operators()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline,
            Utc("2026-11-18T09:00:00"), Utc("2026-11-18T09:05:00")) };
        var activity = new Interval(Utc("2026-11-18T08:00:00"), Utc("2026-11-18T10:00:00"));

        ShiftOfflineFallback.IsRequired(raw, activity, ShiftStart, FactEnd, hasOperators: true)
            .Should().BeTrue();
        ShiftOfflineFallback.IsRequired(raw, activity, ShiftStart, FactEnd, hasOperators: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Offline_outside_task_activity_does_not_require_fallback()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline, ShiftStart, ShiftStart.AddMinutes(30)) };
        var activity = new Interval(ShiftStart.AddHours(1), FactEnd);

        IsRequired(raw, activity).Should().BeFalse();
    }

    [Fact]
    public void Offline_after_fact_end_does_not_require_fallback()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline, FactEnd, FactEnd.AddMinutes(30)) };

        IsRequired(raw, Activity).Should().BeFalse();
    }

    [Fact]
    public void Offline_before_shift_start_does_not_require_fallback()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline,
            ShiftStart.AddMinutes(-30), ShiftStart) };
        var activity = new Interval(ShiftStart.AddHours(-1), FactEnd);

        IsRequired(raw, activity).Should().BeFalse();
    }

    [Fact]
    public void Online_idle_without_cycles_does_not_require_fallback()
    {
        var raw = new[] { new RawSegment(ImmStatus.Idle, ShiftStart, FactEnd) };

        IsRequired(raw, Activity).Should().BeFalse();
    }

    [Fact]
    public void Missing_raw_segments_do_not_require_fallback()
    {
        IsRequired(Array.Empty<RawSegment>(), Activity).Should().BeFalse();
    }

    [Fact]
    public void Missing_task_activity_does_not_require_fallback()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline, ShiftStart, FactEnd) };

        ShiftOfflineFallback.IsRequired(raw, null, ShiftStart, FactEnd, hasOperators: true)
            .Should().BeFalse();
    }

    private static bool IsRequired(IEnumerable<RawSegment> raw, Interval? activity) =>
        ShiftOfflineFallback.IsRequired(raw, activity, ShiftStart, FactEnd, hasOperators: true);

    private static DateTime Utc(string value) =>
        DateTime.SpecifyKind(DateTime.Parse(value), DateTimeKind.Utc);
}
