namespace Wintime.Control.Core.Enums;

/// <summary>
/// Проекция <see cref="TaskStatus"/> на «активность» задания для конвейера телеметрии.
/// Активным считается задание в статусе Setup (наладка) или InProgress (производство);
/// всё остальное (нет задания, Draft/Issued/Completed/Closed) — None.
/// </summary>
public enum ActiveTaskStatus
{
    None,
    Setup,
    InProgress
}

/// <summary>
/// Маппинг доменного <see cref="TaskStatus"/> в <see cref="ActiveTaskStatus"/>.
/// Единственная точка, определяющая, какое задание «активно» для обработки телеметрии.
/// </summary>
public static class ActiveTaskStatusMap
{
    public static ActiveTaskStatus From(TaskStatus? status) => status switch
    {
        TaskStatus.Setup => ActiveTaskStatus.Setup,
        TaskStatus.InProgress => ActiveTaskStatus.InProgress,
        _ => ActiveTaskStatus.None
    };
}
