namespace Wintime.Control.Core.Enums;

/// <summary>
/// Тип события. Создаются только простои (ручные — DowntimeController, авто — DowntimeDetectionWorker).
/// Наладка — интервал задания (SetupStartedAt…StartedAt), авария — статус ТПА, циклы — ImmCycle.
/// Колонка хранится как int; новые типы добавлять осознанно, со значением > 0.
/// </summary>
public enum EventType
{
    Downtime = 0
}
