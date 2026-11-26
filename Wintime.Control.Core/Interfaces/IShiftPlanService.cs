using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftPlanService
{
    Task<IReadOnlyList<ShiftPlanDayDto>> GetAsync(
        DateOnly from, DateOnly to, string callerUserId, CancellationToken ct);
    Task<ShiftPlanDayDto> SetLeadAsync(
        DateOnly date, int position, string? shiftLeadId, string callerUserId, CancellationToken ct);
    Task<ShiftPlanDayDto> MaterializeAsync(
        DateOnly date, int position, string callerUserId, CancellationToken ct);
    Task<IReadOnlyList<StaffPersonDto>> GetLeadCandidatesAsync(CancellationToken ct);
    Task SynchronizeFutureDaysAsync(
        IReadOnlyList<Shift> newSchedule, DateTime nowUtc, CancellationToken ct);
}
