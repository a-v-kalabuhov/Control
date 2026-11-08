using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/shift-plan")]
[Authorize(Policy = "ManagerOrAdmin")]
public class ShiftPlanController : ControllerBase
{
    private readonly IShiftPlanService _service;

    public ShiftPlanController(IShiftPlanService service) => _service = service;

    [HttpGet]
    public Task<IReadOnlyList<ShiftPlanDayDto>> Get(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        _service.GetAsync(from, to, ct);

    [HttpGet("lead-candidates")]
    public Task<IReadOnlyList<StaffPersonDto>> Candidates(CancellationToken ct) =>
        _service.GetLeadCandidatesAsync(ct);

    [HttpPut("days/{date}/items/{position:int}/lead")]
    public Task<ShiftPlanDayDto> SetLead(
        DateOnly date, int position, [FromBody] SetShiftLeadRequest request, CancellationToken ct) =>
        _service.SetLeadAsync(date, position, request.ShiftLeadId, ct);
}
