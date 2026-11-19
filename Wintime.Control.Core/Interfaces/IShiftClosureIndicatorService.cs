using Wintime.Control.Core.DTOs.ShiftPlans;

namespace Wintime.Control.Core.Interfaces;

public sealed record ShiftClosureIndicator(
    ShiftClosureStatus? ClosureStatus,
    bool HasOrphanOutput);

public interface IShiftClosureIndicatorService
{
    Task<IReadOnlyDictionary<Guid, ShiftClosureIndicator>> GetAsync(
        IReadOnlyCollection<Guid> itemIds, DateTime nowUtc, CancellationToken ct);
}
