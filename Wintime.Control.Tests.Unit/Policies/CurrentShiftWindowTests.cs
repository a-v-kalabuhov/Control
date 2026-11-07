using FluentAssertions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class CurrentShiftWindowTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static Shift Shift(int startMinutes, int durationMinutes) => new()
    {
        StartMinutes = startMinutes,
        DurationMinutes = durationMinutes
    };

    [Fact]
    public void Resolve_ReturnsActiveDayShift()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var shifts = new[] { Shift(8 * 60, 9 * 60) };

        var window = CurrentShiftWindow.Resolve(now, shifts, Utc);

        window.Start.Should().Be(new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc));
        window.End.Should().Be(new DateTime(2026, 11, 7, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_ReturnsOvernightShiftStartedYesterday()
    {
        var now = new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc);
        var shifts = new[] { Shift(22 * 60, 8 * 60) };

        var window = CurrentShiftWindow.Resolve(now, shifts, Utc);

        window.Start.Should().Be(new DateTime(2026, 11, 6, 22, 0, 0, DateTimeKind.Utc));
        window.End.Should().Be(new DateTime(2026, 11, 7, 6, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_ReturnsLatestCompletedShiftOutsideWorkingHours()
    {
        var now = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc);
        var shifts = new[] { Shift(8 * 60, 9 * 60) };

        var window = CurrentShiftWindow.Resolve(now, shifts, Utc);

        window.Start.Should().Be(new DateTime(2026, 11, 7, 8, 0, 0, DateTimeKind.Utc));
        window.End.Should().Be(new DateTime(2026, 11, 7, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_BeforeFirstShift_ReturnsPreviousDayShift()
    {
        var now = new DateTime(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc);
        var shifts = new[] { Shift(8 * 60, 9 * 60) };

        var window = CurrentShiftWindow.Resolve(now, shifts, Utc);

        window.Start.Should().Be(new DateTime(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc));
        window.End.Should().Be(new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_UsesFactoryTimezone()
    {
        var factoryTz = TimeZoneInfo.CreateCustomTimeZone(
            "test-utc+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
        var now = new DateTime(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc); // 12:00 на заводе
        var shifts = new[] { Shift(8 * 60, 9 * 60) };

        var window = CurrentShiftWindow.Resolve(now, shifts, factoryTz);

        window.Start.Should().Be(new DateTime(2026, 11, 7, 5, 0, 0, DateTimeKind.Utc));
        window.End.Should().Be(new DateTime(2026, 11, 7, 14, 0, 0, DateTimeKind.Utc));
    }
}
