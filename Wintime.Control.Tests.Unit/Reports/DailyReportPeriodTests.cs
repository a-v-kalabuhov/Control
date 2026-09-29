using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Reports;

namespace Wintime.Control.Tests.Unit.Reports;

public class DailyReportPeriodTests
{
    private const string Moscow = "Europe/Moscow"; // UTC+3, без перехода на летнее время

    private static Shift MakeShift(int startMinutes, int durationMinutes, string tz = Moscow) => new()
    {
        Id = Guid.NewGuid(),
        StartMinutes = startMinutes,
        DurationMinutes = durationMinutes,
        TimeZoneId = tz
    };

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) =>
        DateTime.SpecifyKind(new DateTime(year, month, day, hour, minute, 0), DateTimeKind.Utc);

    private static readonly DateTime Date = new(2026, 9, 29);

    [Fact]
    public void NoShift_PeriodIsLocalDay_InShiftTimeZone()
    {
        var p = DailyReportPeriod.Resolve(Date, shift: null, timeZoneId: Moscow);

        Assert.Equal(Utc(2026, 9, 28, 21), p.PeriodStart); // 29.09 00:00 МСК
        Assert.Equal(Utc(2026, 9, 29, 21), p.PeriodEnd);   // 30.09 00:00 МСК
        Assert.Equal(DateTimeKind.Utc, p.PeriodStart.Kind);
    }

    [Fact]
    public void NoShift_TimelineWindowEqualsPeriod()
    {
        var p = DailyReportPeriod.Resolve(Date, shift: null, timeZoneId: Moscow);

        Assert.Equal(p.PeriodStart, p.TimelineStart);
        Assert.Equal(p.PeriodEnd, p.TimelineEnd);
    }

    [Fact]
    public void Shift_PeriodIsShiftLocalTime_ConvertedToUtc()
    {
        var p = DailyReportPeriod.Resolve(Date, MakeShift(480, 540), Moscow); // 08:00–17:00 МСК

        Assert.Equal(Utc(2026, 9, 29, 5), p.PeriodStart);
        Assert.Equal(Utc(2026, 9, 29, 14), p.PeriodEnd);
    }

    [Fact]
    public void Shift_TimelineWindowExtendsByHalfHourOnBothSides()
    {
        var p = DailyReportPeriod.Resolve(Date, MakeShift(480, 540), Moscow);

        Assert.Equal(Utc(2026, 9, 29, 4, 30), p.TimelineStart);
        Assert.Equal(Utc(2026, 9, 29, 14, 30), p.TimelineEnd);
    }

    [Fact]
    public void NightShift_EndsNextDay()
    {
        var p = DailyReportPeriod.Resolve(Date, MakeShift(1320, 480), Moscow); // 22:00–06:00 МСК

        Assert.Equal(Utc(2026, 9, 29, 19), p.PeriodStart);
        Assert.Equal(Utc(2026, 9, 30, 3), p.PeriodEnd);
    }

    [Fact]
    public void Shift_UsesOwnTimeZone()
    {
        var p = DailyReportPeriod.Resolve(Date, MakeShift(480, 540, tz: "UTC"), Moscow);

        Assert.Equal(Utc(2026, 9, 29, 8), p.PeriodStart);
    }
}
