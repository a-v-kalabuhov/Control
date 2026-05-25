namespace Wintime.Control.Core.Entities;

/// <summary>
/// Запись в системной конфигурации (ключ-значение).
/// Используется для хранения платформенных параметров: InstallationId,
/// MaintenanceModeActive, TokenInvalidatedBefore и т.п.
/// Key является PK.
/// </summary>
public class SystemConfigEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
