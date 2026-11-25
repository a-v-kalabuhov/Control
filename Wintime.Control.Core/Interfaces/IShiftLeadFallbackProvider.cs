using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftLeadFallbackProvider
{
    Task<ShiftLeadFallbackSnapshot> GetAsync(CancellationToken ct);
}
