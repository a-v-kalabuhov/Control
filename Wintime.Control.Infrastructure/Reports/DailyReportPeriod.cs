using Wintime.Control.Core.Entities;

namespace Wintime.Control.Infrastructure.Reports;

/// <summary>
/// Границы отчёта "Картина рабочего дня" в UTC.
/// Period — окно агрегатов (работа/простой/выпуск); Timeline — окно графика.
/// Время смен (<see cref="Shift.StartMinutes"/>) — локальное время завода в <see cref="Shift.TimeZoneId"/>,
/// поэтому сутки и смена считаются в этой зоне, а не в UTC.
/// </summary>
internal readonly record struct DailyReportPeriod(
    DateTime PeriodStart, DateTime PeriodEnd, DateTime TimelineStart, DateTime TimelineEnd)
{
    // Поля на графике смены: ось фронта — смена ±30 мин, данные должны её покрывать
    private static readonly TimeSpan ShiftTimelineMargin = TimeSpan.FromMinutes(30);

    /// <param name="date">Отчётная дата (локальная дата завода).</param>
    /// <param name="shift">Выбранная смена или null — тогда отчёт за локальные сутки.</param>
    /// <param name="timeZoneId">Зона завода для суток без смены.</param>
    internal static DailyReportPeriod Resolve(DateTime date, Shift? shift, string timeZoneId)
    {
        if (shift == null)
        {
            var day = FactoryCalendar.Days(date, date, timeZoneId)[0];
            return new DailyReportPeriod(day.StartUtc, day.EndUtc, day.StartUtc, day.EndUtc);
        }

        var localDay = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        var shiftTz = TimeZoneInfo.FindSystemTimeZoneById(shift.TimeZoneId);
        var start = TimeZoneInfo.ConvertTimeToUtc(localDay.AddMinutes(shift.StartMinutes), shiftTz);
        var end = start.AddMinutes(shift.DurationMinutes);
        return new DailyReportPeriod(start, end, start - ShiftTimelineMargin, end + ShiftTimelineMargin);
    }
}
