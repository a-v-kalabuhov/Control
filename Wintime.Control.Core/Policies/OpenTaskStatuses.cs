using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Незавершённые статусы задания (PZP-07b): задание ещё держит ТПА и пресс-форму. Такие задания участвуют
/// в конфликтах плана и не дают перевести ТПА/ПФ в архив. Сравнение — только <c>==</c> (Setup = 5 вне порядка ЖЦ).
/// </summary>
public static class OpenTaskStatuses
{
    public static readonly TaskStatus[] All = { TaskStatus.Planned, TaskStatus.Issued, TaskStatus.Setup, TaskStatus.InProgress };

    public static bool Contains(TaskStatus status) =>
        status == TaskStatus.Planned || status == TaskStatus.Issued
        || status == TaskStatus.Setup || status == TaskStatus.InProgress;
}
