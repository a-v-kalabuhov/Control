namespace Wintime.Control.Core.Constants;

/// <summary>
/// Канонические значения режима работы ТПА в wire-формате MQTT (поле <c>Mode</c>).
/// Всегда нижний регистр. Коннекторы могут прислать значение в произвольном
/// регистре — поэтому любое сравнение режима обязано идти через <see cref="Normalize"/>,
/// а не напрямую со строковым литералом.
/// </summary>
public static class ImmMode
{
    public const string Auto = "auto";
    public const string Manual = "manual";
    public const string Idle = "idle";
    public const string Alarm = "alarm";

    /// <summary>
    /// Приводит произвольное значение <c>Mode</c> к канону: trimmed, нижний регистр.
    /// Для <c>null</c> возвращает пустую строку.
    /// </summary>
    public static string Normalize(string? mode) =>
        mode?.Trim().ToLowerInvariant() ?? string.Empty;
}
