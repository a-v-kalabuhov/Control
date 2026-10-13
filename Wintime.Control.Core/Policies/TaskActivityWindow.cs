using System.Linq.Expressions;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Окно активности задания (ARCH-03, ADR-0016): <c>[SetupStartedAt, CompletedAt ?? ClosedAt ?? now)</c>.
/// Задание относится к периоду, если его окно активности пересекается с периодом — а не по одной
/// точке (<c>StartedAt</c>/<c>IssuedAt</c>): задание может идти через несколько смен и суток.
/// Окно выпуска — то же, но от <c>StartedAt</c>. Не хранится, считается на лету.
/// </summary>
public static class TaskActivityWindow
{
    /// <summary>Окно активности; <c>null</c>, если наладка не начиналась.</summary>
    public static Interval? Activity(ShiftTask task, DateTime nowUtc) => Build(task.SetupStartedAt, task, nowUtc);

    /// <summary>Окно выпуска; <c>null</c>, если выпуск не начинался.</summary>
    public static Interval? Release(ShiftTask task, DateTime nowUtc) => Build(task.StartedAt, task, nowUtc);

    /// <summary>
    /// EF-выражение «окно активности пересекается с <c>[fromUtc, toUtc)</c>». Единственный способ
    /// отбирать задания по периоду в запросах — чтобы условие не расходилось с <see cref="Activity"/>.
    /// Открытое окно (не завершено и не закрыто) тянется до «сейчас» и пересекает любой период после начала.
    /// </summary>
    public static Expression<Func<ShiftTask, bool>> OverlapsPeriod(DateTime fromUtc, DateTime toUtc) =>
        t => t.SetupStartedAt != null && t.SetupStartedAt < toUtc
             && ((t.CompletedAt == null && t.ClosedAt == null) || (t.CompletedAt ?? t.ClosedAt) > fromUtc);

    private static Interval? Build(DateTime? start, ShiftTask task, DateTime nowUtc)
    {
        if (start is not DateTime s)
            return null;
        var end = task.CompletedAt ?? task.ClosedAt ?? nowUtc;
        return new Interval(s, end < s ? s : end);
    }
}
