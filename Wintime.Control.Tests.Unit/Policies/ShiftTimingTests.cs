using FluentAssertions;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Tests.Unit.Policies;

public class ShiftTimingTests
{
    [Theory]
    [InlineData("2026-11-08T04:59:59Z", "Future")]
    [InlineData("2026-11-08T05:00:00Z", "Current")]
    [InlineData("2026-11-08T12:59:59Z", "Current")]
    [InlineData("2026-11-08T13:00:00Z", "Historical")]
    public void Resolve_uses_half_open_interval(string now, string expected)
    {
        var state = ShiftTiming.Resolve(
            DateTime.Parse("2026-11-08T05:00:00Z").ToUniversalTime(),
            DateTime.Parse("2026-11-08T13:00:00Z").ToUniversalTime(),
            DateTime.Parse(now).ToUniversalTime());
        state.ToString().Should().Be(expected);
    }
}
