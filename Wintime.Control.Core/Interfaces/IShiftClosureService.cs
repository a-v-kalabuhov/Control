using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.DTOs.ShiftStaffing;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftClosureService
{
    Task<ShiftClosureDto> GetAsync(Guid itemId, StaffingCaller caller, CancellationToken ct);
    Task<ShiftClosureDto> SaveDraftAsync(
        Guid itemId, SaveShiftClosureDraftRequest request, StaffingCaller caller, CancellationToken ct);
    Task<ShiftClosureDto> CloseAsync(
        Guid itemId, CloseShiftClosureRequest request, StaffingCaller caller, CancellationToken ct);
    Task<IReadOnlyList<ShiftClosurePendingDto>> GetPendingAsync(
        StaffingCaller caller, CancellationToken ct);
    Task<ShiftClosureDto> GetClosedAsync(Guid closureId, StaffingCaller caller, CancellationToken ct);
    Task CorrectAsync(
        Guid closureId, CorrectShiftClosureRequest request, StaffingCaller caller, CancellationToken ct);
    Task<IReadOnlyList<ShiftClosureCorrectionDto>> GetCorrectionsAsync(
        Guid closureId, StaffingCaller caller, CancellationToken ct);
}
