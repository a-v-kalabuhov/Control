namespace Wintime.Control.SDK.Licensing;

/// <summary>
/// Кэш статусов лицензий всех загруженных модулей. Используется MQTT-диспетчером
/// для быстрой проверки без обращения к БД на каждое сообщение.
/// </summary>
public interface IModuleLicenseCache
{
    LicenseStatus GetStatus(string moduleKey);
    void Set(string moduleKey, IModuleLicense license, int currentEquipmentCount);

    /// <summary>
    /// Вызывается при добавлении или удалении оборудования через API.
    /// </summary>
    void InvalidateEquipmentCount(string moduleKey, int newCount);
}
