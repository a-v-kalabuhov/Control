using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.DTOs.ShiftStaffing;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftClosureService
{
    Task<ShiftClosureDto> GetAsync(Guid itemId, StaffingCaller caller, CancellationToken ct);
    Task<IReadOnlyList<ShiftClosurePendingDto>> GetPendingAsync(
        StaffingCaller caller, CancellationToken ct);
    Task<ShiftClosureDto> GetClosedAsync(Guid closureId, StaffingCaller caller, CancellationToken ct);
}
