using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class PlannedTaskStartTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly DateOnly Date = new(2026, 10, 15);

    [Fact]
    public void Resolve_WithTime_IsDatePlusTimeInFactoryZone()
    {
        var result = PlannedTaskStart.Resolve(Date, new TimeOnly(14, 0), 480, Moscow);

        result.Should().Be(new DateTime(2026, 10, 15, 11, 0, 0, DateTimeKind.Utc));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Resolve_WithoutTime_IsFirstShiftStart()
    {
        var result = PlannedTaskStart.Resolve(Date, null, 480, Moscow); // 08:00 МСК

        result.Should().Be(new DateTime(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_WithoutTimeAndShifts_IsLocalMidnight()
    {
        var result = PlannedTaskStart.Resolve(Date, null, null, Moscow);

        result.Should().Be(new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolve_TimeInDstGap_MovesForwardToFirstValidTime()
    {
        // 29.03.2026 в Берлине 02:00–03:00 не существует (переход на летнее время).
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        var result = PlannedTaskStart.Resolve(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), null, berlin);

        result.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc)); // 03:00 CEST
    }

    [Fact]
    public void FactorySchedule_PlannedStartUtc_DelegatesToResolve()
    {
        var schedule = new FactorySchedule(Moscow, 480);

        schedule.PlannedStartUtc(Date, null)
            .Should().Be(PlannedTaskStart.Resolve(Date, null, 480, Moscow));
    }

    [Fact]
    public void FactorySchedule_ToLocal_ConvertsUtcToFactoryWallClock()
    {
        var schedule = new FactorySchedule(Moscow, 480);

        schedule.ToLocal(new DateTime(2026, 10, 15, 21, 30, 0, DateTimeKind.Utc))
            .Should().Be(new DateTime(2026, 10, 16, 0, 30, 0));
    }
}
