using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public class SecuritySettingsService : ISecuritySettingsService
{
    private readonly ControlDbContext _db;

    public SecuritySettingsService(ControlDbContext db) => _db = db;

    public Task<SecuritySettings> GetAsync(CancellationToken ct = default) =>
        _db.SecuritySettings.SingleAsync(s => s.Id == SecuritySettings.SingletonId, ct);

    public async Task<SecuritySettings> UpdateAsync(bool allowWeakPasswords, string? userId,
                                                    CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);
        settings.Update(allowWeakPasswords, userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
        return settings;
    }
}
