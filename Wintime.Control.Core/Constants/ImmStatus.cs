namespace Wintime.Control.Core.Constants;

/// <summary>
/// Канонические значения статуса ТПА, которые хранятся в <c>ImmStatusHistory</c>,
/// кешируются в <c>IImmStatusCache</c> и отдаются фронтенду. В отличие от
/// <see cref="ImmMode"/> (wire-формат) — это формат отображения, с заглавной буквы.
/// Соответствие режим → статус описано в CLAUDE.md (таблица статусов ТПА).
/// </summary>
public static class ImmStatus
{
    public const string Auto = "Auto";
    public const string Manual = "Manual";
    public const string Idle = "Idle";
    public const string Alarm = "Alarm";
    public const string Offline = "Offline";
}
