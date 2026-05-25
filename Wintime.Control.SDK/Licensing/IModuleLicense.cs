namespace Wintime.Control.SDK.Licensing;

/// <summary>
/// Описывает лицензию на использование модуля.
/// Реализации проверяют RSA-подпись и привязку к InstallationId.
/// </summary>
public interface IModuleLicense
{
    string ModuleKey { get; }
    string CustomerId { get; }
    string InstallationId { get; }
    DateTime ExpiresAt { get; }

    /// <summary>
    /// Максимальное количество единиц оборудования. -1 означает без ограничений.
    /// </summary>
    int MaxEquipmentCount { get; }

    bool IsValid { get; }
    string? ValidationError { get; }
}
