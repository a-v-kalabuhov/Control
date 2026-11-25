using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftLeadFallbackProvider : IShiftLeadFallbackProvider
{
    private readonly ControlDbContext _context;

    public ShiftLeadFallbackProvider(ControlDbContext context) => _context = context;

    public async Task<ShiftLeadFallbackSnapshot> GetAsync(CancellationToken ct)
    {
        var defaults = await _context.ShiftDefaultLeads.AsNoTracking().ToListAsync(ct);
        var users = await _context.Users.AsNoTracking().ToListAsync(ct);
        var firstActiveManagerId = await _context.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRole.Manager)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        return new ShiftLeadFallbackSnapshot(
            defaults.ToDictionary(d => d.Position, d => d.ShiftLeadId),
            firstActiveManagerId,
            users.ToDictionary(u => u.Id));
    }
}
