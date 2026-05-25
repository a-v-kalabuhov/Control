using Wintime.Control.SDK.Licensing;

namespace Wintime.Control.Infrastructure.Plugins;

/// <summary>
/// Заглушка кэша лицензий — всегда разрешает обработку.
/// Будет заменена реальной реализацией при внедрении системы лицензирования.
/// </summary>
public class NoOpModuleLicenseCache : IModuleLicenseCache
{
    private static readonly LicenseStatus ValidStatus = new(true, false);

    public LicenseStatus GetStatus(string moduleKey) => ValidStatus;

    public void Set(string moduleKey, IModuleLicense license, int currentEquipmentCount) { }

    public void InvalidateEquipmentCount(string moduleKey, int newCount) { }
}
