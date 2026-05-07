namespace Wintime.Control.Core.Cache;

/// <summary>
/// Кешированные данные ТПА: последние значения датчиков и время последнего сообщения.
/// </summary>
public sealed record ImmCacheEntry(
    Guid ImmId,
    DateTime LastMessageAt,
    int TimeoutSeconds,
    IReadOnlyDictionary<string, string> SensorValues
)
{
    /// <summary>
    /// True если с момента последнего сообщения прошло меньше <see cref="TimeoutSeconds"/>.
    /// False также для новых записей, у которых <see cref="LastMessageAt"/> == <see cref="DateTime.MinValue"/>.
    /// </summary>
    public bool IsOnline =>
        LastMessageAt != DateTime.MinValue &&
        (DateTime.UtcNow - LastMessageAt).TotalSeconds < TimeoutSeconds;
}
