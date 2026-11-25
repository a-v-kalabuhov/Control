using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftClosureIndicatorService : IShiftClosureIndicatorService
{
    private readonly ControlDbContext _context;
    private readonly ShiftClosureReadModelLoader _loader;
    private readonly IShiftLeadFallbackProvider _fallbackProvider;

    public ShiftClosureIndicatorService(
        ControlDbContext context,
        ShiftClosureReadModelLoader loader,
        IShiftLeadFallbackProvider fallbackProvider)
    {
        _context = context;
        _loader = loader;
        _fallbackProvider = fallbackProvider;
    }

    public async Task<IReadOnlyDictionary<Guid, ShiftClosureIndicator>> GetAsync(
        IReadOnlyCollection<Guid> itemIds, DateTime nowUtc, CancellationToken ct)
    {
        if (itemIds.Count == 0)
            return new Dictionary<Guid, ShiftClosureIndicator>();

        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var items = await _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Closure)
            .Where(i => itemIds.Contains(i.Id))
            .ToListAsync(ct);
        var openWithFact = items
            .Where(i => i.Closure?.State != ShiftClosureState.Closed
                && DateTime.SpecifyKind(i.StartAtUtc, DateTimeKind.Utc) < nowUtc)
            .ToList();
        var orphanByItem = await _loader.GetOrphanOutputByItemAsync(openWithFact, nowUtc, ct);
        var fallback = await _fallbackProvider.GetAsync(ct);

        return items.ToDictionary(i => i.Id, i =>
        {
            var isClosed = i.Closure?.State == ShiftClosureState.Closed;
            var hasEffectiveLead = EffectiveShiftLeadResolver.Resolve(i, fallback).Id != null;
            var ended = nowUtc >= DateTime.SpecifyKind(i.EndAtUtc, DateTimeKind.Utc);
            ShiftClosureStatus? status = isClosed
                ? ShiftClosureStatus.Closed
                : ended
                    ? ShiftClosureStatus.NotClosed
                    : hasEffectiveLead
                        ? ShiftClosureStatus.Draft
                        : null;
            return new ShiftClosureIndicator(
                status,
                !isClosed && orphanByItem.GetValueOrDefault(i.Id));
        });
    }
}
