using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Cache;

/// <summary>
/// Шаблон оборудования из кеша.
/// </summary>
/// <param name="Id">Id из базы данных.</param>
/// <param name="Name">Название шаблона</param>
/// <param name="UpdatedAt">Время последнего изменения</param>
/// <param name="DeviceTimeoutSeconds">Таймаут в секундах</param>
/// <param name="Sensors">Список шаблнов датчиков</param>
/// <remarks>
/// Потокобезопасен.
/// </remarks>
public sealed record CachedTemplate(
    Guid Id,
    string Name,
    DateTime UpdatedAt,
    int DeviceTimeoutSeconds,
    IReadOnlyList<SensorTemplate> Sensors
);
