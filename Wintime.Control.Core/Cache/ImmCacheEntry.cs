using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Core.Cache;

/// <summary>
/// Кешированные данные ТПА: последние значения датчиков, время последнего сообщения
/// и последний завершённый цикл литья из MQTT.
/// </summary>
public sealed record ImmCacheEntry(
    Guid ImmId,
    DateTime LastMessageAt,
    int TimeoutSeconds,
    IReadOnlyDictionary<string, string> SensorValues
)
{
    /// <summary>
    /// Последний завершённый цикл — блок <c>lastCycle</c> из MQTT (контракт v2, ADR-0012).
    /// <c>null</c>, пока с момента старта API не пришло ни одного сообщения с <c>lastCycle</c>.
    /// </summary>
    public CompletedCycleSnapshot? LastCycle { get; init; }

    /// <summary>
    /// Длительность последнего завершённого цикла, секунды (<c>EndTime − StartTime</c>);
    /// <c>null</c> — цикл ещё неизвестен.
    /// </summary>
    public double? LastCycleDurationSeconds
        => LastCycle is { } c ? (c.EndTime - c.StartTime).TotalSeconds : null;

    /// <summary>
    /// True если с момента последнего сообщения прошло меньше <see cref="TimeoutSeconds"/>.
    /// False также для новых записей, у которых <see cref="LastMessageAt"/> == <see cref="DateTime.MinValue"/>.
    /// </summary>
    public bool IsOnline
    {
        get
        {
            if (LastMessageAt == DateTime.MinValue)
                return false;
            var period = (DateTime.UtcNow - LastMessageAt).TotalSeconds;
            return period < TimeoutSeconds;
        }
    }
}
