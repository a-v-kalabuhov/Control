using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftClosureService : IShiftClosureService
{
    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly ShiftClosureReadModelLoader _loader;

    public ShiftClosureService(
        ControlDbContext context, IFactoryClock factoryClock, ShiftClosureReadModelLoader loader)
    {
        _context = context;
        _factoryClock = factoryClock;
        _loader = loader;
    }

    public Task<ShiftClosureDto> GetAsync(
        Guid itemId, StaffingCaller caller, CancellationToken ct) =>
        _loader.LoadAsync(itemId, caller, Now, ct);

    public async Task<IReadOnlyList<ShiftClosurePendingDto>> GetPendingAsync(
        StaffingCaller caller, CancellationToken ct)
    {
        var now = Now;
        var query = _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Include(i => i.ShiftLead)
            .Include(i => i.Closure)
            .Where(i => i.ShiftLeadId != null && i.EndAtUtc <= now
                && (i.Closure == null || i.Closure.State != ShiftClosureState.Closed));
        if (caller.IsShiftLead)
            query = query.Where(i => i.ShiftLeadId == caller.UserId);
        var items = await query.OrderByDescending(i => i.StartAtUtc).ThenByDescending(i => i.Id)
            .ToListAsync(ct);
        var result = new List<ShiftClosurePendingDto>(items.Count);
        foreach (var item in items)
        {
            var report = await _loader.LoadAsync(item.Id, caller, now, ct);
            result.Add(new ShiftClosurePendingDto
            {
                ShiftPlanItemId = item.Id,
                ShiftDate = item.Day.ShiftDate,
                Position = item.Position,
                StartAtUtc = item.StartAtUtc,
                EndAtUtc = item.EndAtUtc,
                TimeZoneId = item.TimeZoneId,
                ShiftLeadId = item.ShiftLeadId!,
                ShiftLeadName = item.ShiftLead?.FullName ?? string.Empty,
                HasOrphanOutput = report.Timelines.SelectMany(t => t.Intervals)
                    .Any(i => i.HasOrphanOutput)
            });
        }
        return result;
    }

    public Task<ShiftClosureDto> GetClosedAsync(
        Guid closureId, StaffingCaller caller, CancellationToken ct) =>
        _loader.LoadClosedAsync(closureId, caller, ct);

    private DateTime Now => DateTime.SpecifyKind(_factoryClock.UtcNow, DateTimeKind.Utc);
}
