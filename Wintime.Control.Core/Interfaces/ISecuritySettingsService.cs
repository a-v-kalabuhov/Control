using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Настройки безопасности (ADR-0015). Без кеша: читаются редко.</summary>
public interface ISecuritySettingsService
{
    Task<SecuritySettings> GetAsync(CancellationToken ct = default);

    Task<SecuritySettings> UpdateAsync(bool allowWeakPasswords, string? userId,
                                       CancellationToken ct = default);
}
