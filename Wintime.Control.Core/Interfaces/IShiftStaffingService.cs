using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.DTOs.ShiftStaffing;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftStaffingService
{
    Task<NearestShiftDto> GetNearestAsync(string userId, CancellationToken ct);
    Task<IReadOnlyList<ShiftStaffingSummaryDto>> GetHistoryAsync(string userId, CancellationToken ct);
    Task<ShiftStaffingItemDto> GetItemAsync(Guid itemId, StaffingCaller caller, CancellationToken ct);
    Task<IReadOnlyList<StaffPersonDto>> GetOperatorsAsync(CancellationToken ct);
}
