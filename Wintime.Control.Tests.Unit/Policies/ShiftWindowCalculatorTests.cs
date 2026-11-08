using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftWindowCalculatorTests
{
    [Fact]
    public void Build_sorts_positions_and_keeps_night_shift_on_start_date()
    {
        var shifts = new[]
        {
            new Shift { StartMinutes = 20 * 60, DurationMinutes = 8 * 60, TimeZoneId = "Europe/Moscow" },
            new Shift { StartMinutes = 8 * 60, DurationMinutes = 8 * 60, TimeZoneId = "Europe/Moscow" }
        };

        var windows = ShiftWindowCalculator.Build(new DateOnly(2026, 11, 8), shifts);

        windows.Select(w => w.Position).Should().Equal(1, 2);
        windows[0].StartAtUtc.Should().Be(DateTime.Parse("2026-11-08T05:00:00Z").ToUniversalTime());
        windows[1].StartAtUtc.Should().Be(DateTime.Parse("2026-11-08T17:00:00Z").ToUniversalTime());
        windows[1].EndAtUtc.Should().Be(DateTime.Parse("2026-11-09T01:00:00Z").ToUniversalTime());
        windows.Should().OnlyContain(w => w.StartAtUtc.Kind == DateTimeKind.Utc && w.EndAtUtc.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public void Build_uses_each_shift_timezone_and_moves_invalid_local_time_forward()
    {
        var shifts = new[]
        {
            new Shift
            {
                StartMinutes = 2 * 60,
                DurationMinutes = 2 * 60,
                TimeZoneId = "America/New_York"
            }
        };

        var windows = ShiftWindowCalculator.Build(new DateOnly(2026, 3, 8), shifts);

        windows.Should().ContainSingle();
        windows[0].StartAtUtc.Should().Be(DateTime.Parse("2026-03-08T07:00:00Z").ToUniversalTime());
        windows[0].EndAtUtc.Should().Be(DateTime.Parse("2026-03-08T08:00:00Z").ToUniversalTime());
        windows[0].TimeZoneId.Should().Be("America/New_York");
        windows.Should().OnlyContain(w => w.StartAtUtc.Kind == DateTimeKind.Utc && w.EndAtUtc.Kind == DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void Build_when_dst_normalization_collapses_window_restores_scheduled_positive_duration(
        int durationMinutes)
    {
        var shifts = new[]
        {
            new Shift
            {
                StartMinutes = 2 * 60,
                DurationMinutes = durationMinutes,
                TimeZoneId = "America/New_York"
            }
        };

        var window = ShiftWindowCalculator.Build(new DateOnly(2027, 3, 14), shifts).Should()
            .ContainSingle().Subject;

        window.StartAtUtc.Should().Be(DateTime.Parse("2027-03-14T07:00:00Z").ToUniversalTime());
        window.EndAtUtc.Should().Be(window.StartAtUtc.AddMinutes(durationMinutes));
        window.StartAtUtc.Should().BeBefore(window.EndAtUtc);
        window.StartAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        window.EndAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }
}
