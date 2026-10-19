namespace Wintime.Control.Core.DTOs.AppInfo;

/// <summary>Публичные сведения о стенде для фронта (без авторизации).</summary>
public class AppInfoDto
{
    /// <summary>Демо-стенд (флаг DemoMode): открытые демо-аккаунты и подпись «демо-версия».</summary>
    public bool DemoMode { get; set; }
}
