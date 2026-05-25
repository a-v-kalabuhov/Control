namespace Wintime.Control.SDK.Licensing;

/// <summary>
/// Кэшированное состояние лицензии модуля для проверки в MQTT-диспетчере.
/// Инициализируется при загрузке лицензии из файла, инвалидируется при
/// изменении количества оборудования через API.
/// </summary>
public record LicenseStatus(
    bool LicenseValid,
    bool EquipmentCountExceeded,
    string? InvalidReason = null
);
