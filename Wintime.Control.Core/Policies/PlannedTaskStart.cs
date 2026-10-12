namespace Wintime.Control.Core.Policies;

/// <summary>
/// Эффективное плановое начало задания (PZP-13): плановая дата + время начала наладки
/// по часам завода; без времени — начало самой ранней смены этого дня; без смен — 00:00.
/// Чистая функция, результат не хранится.
/// </summary>
public static class PlannedTaskStart
{
    public static DateTime Resolve(DateOnly date, TimeOnly? time,
                                   int? firstShiftStartMinutes, TimeZoneInfo factoryTz)
    {
        var localTime = time
            ?? (firstShiftStartMinutes is int minutes
                ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes))
                : TimeOnly.MinValue);

        var local = date.ToDateTime(localTime, DateTimeKind.Unspecified);

        // Время из «дыры» перехода на летнее время не существует — сдвигаем вперёд до валидного.
        while (factoryTz.IsInvalidTime(local))
            local = local.AddMinutes(15);

        return TimeZoneInfo.ConvertTimeToUtc(local, factoryTz);
    }
}

/// <summary>Расписание завода, нужное для планового начала: пояс и начало самой ранней смены.</summary>
public sealed record FactorySchedule(TimeZoneInfo TimeZone, int? FirstShiftStartMinutes)
{
    public DateTime PlannedStartUtc(DateOnly date, TimeOnly? time) =>
        PlannedTaskStart.Resolve(date, time, FirstShiftStartMinutes, TimeZone);

    /// <summary>UTC → время по часам завода (Kind=Unspecified).</summary>
    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);
}
