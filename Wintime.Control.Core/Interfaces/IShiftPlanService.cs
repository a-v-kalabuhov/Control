using Wintime.Control.Core.DTOs.ShiftPlans;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftPlanService
{
    Task<IReadOnlyList<ShiftPlanDayDto>> GetAsync(DateOnly from, DateOnly to, CancellationToken ct);
    Task<ShiftPlanDayDto> SetLeadAsync(
        DateOnly date, int position, string? shiftLeadId, CancellationToken ct);
    Task<IReadOnlyList<StaffPersonDto>> GetLeadCandidatesAsync(CancellationToken ct);
}
