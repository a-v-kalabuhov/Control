namespace Wintime.Control.Core.Entities;

/// <summary>
/// Запись в реестре модулей. Key является естественным PK.
/// Авторитетный источник истины о том, какие модули активны в системе.
/// PluginLoader загружает только те модули, у которых IsEnabled = true.
/// </summary>
public class AppModuleRecord
{
    public string Key { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string? InstalledVersion { get; set; }
    public DateTime? EnabledAt { get; set; }
    public DateTime? DisabledAt { get; set; }
    public bool RetainDataOnDisable { get; set; } = true;
}
