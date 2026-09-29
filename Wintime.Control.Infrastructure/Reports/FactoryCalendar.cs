namespace Wintime.Control.Infrastructure.Reports;

/// <summary>
/// Календарные сутки завода: отчётная дата — локальная дата в зоне смен (<c>Shift.TimeZoneId</c>),
/// её границы переводятся в UTC для запросов к БД.
/// </summary>
internal static class FactoryCalendar
{
    internal const string DefaultTimeZoneId = "Europe/Moscow";

    /// <summary>Начало локальных суток <paramref name="localDate"/> в UTC.</summary>
    internal static DateTime DayStartUtc(DateTime localDate, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified), tz);

    /// <summary>Локальные сутки с <paramref name="dateFrom"/> по <paramref name="dateTo"/> включительно.</summary>
    internal static List<FactoryDay> Days(DateTime dateFrom, DateTime dateTo, string timeZoneId)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var days = new List<FactoryDay>();
        for (var d = dateFrom.Date; d <= dateTo.Date; d = d.AddDays(1))
        {
            days.Add(new FactoryDay(
                DateTime.SpecifyKind(d, DateTimeKind.Unspecified),
                DayStartUtc(d, tz),
                DayStartUtc(d.AddDays(1), tz)));
        }
        return days;
    }
}

/// <param name="Date">Локальная дата завода.</param>
internal readonly record struct FactoryDay(DateTime Date, DateTime StartUtc, DateTime EndUtc);
