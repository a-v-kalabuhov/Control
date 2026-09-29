using Wintime.Control.Infrastructure.Reports;

namespace Wintime.Control.Tests.Unit.Reports;

public class FactoryCalendarTests
{
    private const string Moscow = "Europe/Moscow"; // UTC+3

    private static DateTime Utc(int year, int month, int day, int hour) =>
        DateTime.SpecifyKind(new DateTime(year, month, day, hour, 0, 0), DateTimeKind.Utc);

    [Fact]
    public void Days_SingleDate_ReturnsLocalDayInUtc()
    {
        var days = FactoryCalendar.Days(new DateTime(2026, 9, 29), new DateTime(2026, 9, 29), Moscow);

        var day = Assert.Single(days);
        Assert.Equal(new DateTime(2026, 9, 29), day.Date);
        Assert.Equal(Utc(2026, 9, 28, 21), day.StartUtc);
        Assert.Equal(Utc(2026, 9, 29, 21), day.EndUtc);
        Assert.Equal(DateTimeKind.Utc, day.StartUtc.Kind);
    }

    [Fact]
    public void Days_Range_IsContiguousAndInclusive()
    {
        var days = FactoryCalendar.Days(new DateTime(2026, 9, 27), new DateTime(2026, 9, 29), Moscow);

        Assert.Equal(3, days.Count);
        Assert.Equal(new DateTime(2026, 9, 27), days[0].Date);
        Assert.Equal(new DateTime(2026, 9, 29), days[2].Date);
        Assert.Equal(days[0].EndUtc, days[1].StartUtc);
        Assert.Equal(days[1].EndUtc, days[2].StartUtc);
    }

    [Fact]
    public void Days_DateToBeforeDateFrom_ReturnsEmpty()
    {
        var days = FactoryCalendar.Days(new DateTime(2026, 9, 29), new DateTime(2026, 9, 28), Moscow);

        Assert.Empty(days);
    }

    [Fact]
    public void Days_DateKindUtc_DateStillTreatedAsLocalDate()
    {
        var days = FactoryCalendar.Days(Utc(2026, 9, 29, 0), Utc(2026, 9, 29, 0), Moscow);

        Assert.Equal(Utc(2026, 9, 28, 21), Assert.Single(days).StartUtc);
    }
}
