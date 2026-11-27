using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

public sealed record TelemetryPeriod(
    DateOnly StartDate,
    DateOnly EndDate,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsCurrent);

public sealed record TelemetryPeriodSet(
    TelemetryPeriod CurrentMonth,
    IReadOnlyList<TelemetryPeriod> RecentWeeks,
    IReadOnlyList<TelemetryPeriod> PreviousMonths);

public static class TelemetryPeriodCalculator
{
    public static TelemetryPeriodSet CreateStatisticsPeriods(DateTime nowUtc, string? timeZoneId)
    {
        nowUtc = RequireUtc(nowUtc);
        var zone = ResolveTimeZone(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var currentMonth = new TelemetryPeriod(
            monthStart, nextMonth.AddDays(-1), LocalStartUtc(monthStart, zone),
            Min(nowUtc, LocalStartUtc(nextMonth, zone)), true);

        var daysFromMonday = ((int)today.DayOfWeek + 6) % 7;
        var currentMonday = today.AddDays(-daysFromMonday);
        var weeks = new List<TelemetryPeriod>(4);
        for (var age = 3; age >= 0; age--)
        {
            var start = currentMonday.AddDays(-7 * age);
            var next = start.AddDays(7);
            weeks.Add(new TelemetryPeriod(
                start, next.AddDays(-1), LocalStartUtc(start, zone),
                age == 0 ? Min(nowUtc, LocalStartUtc(next, zone)) : LocalStartUtc(next, zone),
                age == 0));
        }

        var months = new List<TelemetryPeriod>(3);
        for (var age = 3; age >= 1; age--)
        {
            var start = monthStart.AddMonths(-age);
            var next = start.AddMonths(1);
            months.Add(new TelemetryPeriod(
                start, next.AddDays(-1), LocalStartUtc(start, zone), LocalStartUtc(next, zone), false));
        }

        return new TelemetryPeriodSet(currentMonth, weeks, months);
    }

    public static DateTime GetCleanupCutoffUtc(DateOnly date, DateTime nowUtc, string? timeZoneId)
    {
        nowUtc = RequireUtc(nowUtc);
        var zone = ResolveTimeZone(timeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone));
        if (date > today)
            throw new DomainException("Дата очистки не может быть в будущем");
        return LocalStartUtc(date, zone);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new DomainException("Укажите часовой пояс");
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException("Неизвестный часовой пояс");
        }
    }

    private static DateTime RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Ожидалось серверное UTC-время", nameof(value));
        return value;
    }

    private static DateTime LocalStartUtc(DateOnly date, TimeZoneInfo zone)
    {
        var localStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(localStart))
            localStart = FirstValidLocalTime(localStart, zone);
        if (zone.IsAmbiguousTime(localStart))
        {
            var earliestUtcOffset = zone.GetAmbiguousTimeOffsets(localStart).Max();
            return DateTime.SpecifyKind(localStart - earliestUtcOffset, DateTimeKind.Utc);
        }
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(localStart, zone), DateTimeKind.Utc);
    }

    private static DateTime FirstValidLocalTime(DateTime invalidLocal, TimeZoneInfo zone)
    {
        var low = invalidLocal;
        var high = invalidLocal.AddDays(1);
        while (high.Ticks - low.Ticks > 1)
        {
            var middle = new DateTime(low.Ticks + (high.Ticks - low.Ticks) / 2, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(middle))
                low = middle;
            else
                high = middle;
        }
        return high;
    }

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
}
