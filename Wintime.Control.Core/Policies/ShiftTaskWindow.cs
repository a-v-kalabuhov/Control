using System.Linq.Expressions;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Отбор заданий в окно смены: плановые — по эффективному плановому началу,
/// остальные — по пересечению фактического окна активности со сменой [StartAtUtc, EndAtUtc).
/// </summary>
public static class ShiftTaskWindow
{
    /// <summary>Широкий EF-предикат активности; точную проверку после загрузки выполняет <see cref="Belongs"/>.</summary>
    public static Expression<Func<ShiftTask, bool>> ActivityOverlaps(
        DateTime shiftStartAtUtc, DateTime shiftEndAtUtc) =>
        TaskActivityWindow.OverlapsPeriod(shiftStartAtUtc, shiftEndAtUtc);

    public static bool Belongs(
        ShiftTask task,
        DateTime plannedStartAtUtc,
        DateTime shiftStartAtUtc,
        DateTime shiftEndAtUtc,
        DateTime nowUtc)
    {
        plannedStartAtUtc = DateTime.SpecifyKind(plannedStartAtUtc, DateTimeKind.Utc);
        shiftStartAtUtc = DateTime.SpecifyKind(shiftStartAtUtc, DateTimeKind.Utc);
        shiftEndAtUtc = DateTime.SpecifyKind(shiftEndAtUtc, DateTimeKind.Utc);
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);

        if (task.Status is TaskStatus.Planned or TaskStatus.Issued)
            return plannedStartAtUtc >= shiftStartAtUtc && plannedStartAtUtc < shiftEndAtUtc;

        var activity = TaskActivityWindow.Activity(task, nowUtc);
        return activity != null && activity.Start < shiftEndAtUtc && activity.End > shiftStartAtUtc;
    }
}
