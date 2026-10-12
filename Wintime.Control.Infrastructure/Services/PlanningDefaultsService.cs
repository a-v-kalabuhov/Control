using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public class PlanningDefaultsService : IPlanningDefaultsService
{
    private readonly ControlDbContext _db;

    public PlanningDefaultsService(ControlDbContext db) => _db = db;

    public Task<PlanningDefaults> GetAsync(CancellationToken ct = default) =>
        _db.PlanningDefaults.SingleAsync(d => d.Id == PlanningDefaults.SingletonId, ct);

    public async Task<PlanningDefaults> UpdateAsync(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                                                    string? userId, CancellationToken ct = default)
    {
        var defaults = await GetAsync(ct);
        defaults.Update(setupMinutes, bufferFixedMinutes, bufferPercent, userId, DateTime.UtcNow);
        await _db.SaveChangesAsync(ct);
        return defaults;
    }
}
