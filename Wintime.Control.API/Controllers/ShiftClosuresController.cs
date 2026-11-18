using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/shift-closures")]
[Authorize(Policy = "ShiftLeadOrManagerOrAdmin")]
public sealed class ShiftClosuresController : ControllerBase
{
    private readonly IShiftClosureService _service;

    public ShiftClosuresController(IShiftClosureService service) => _service = service;

    [HttpGet("items/{itemId:guid}")]
    public Task<ShiftClosureDto> Item(Guid itemId, CancellationToken ct) =>
        _service.GetAsync(itemId, Caller, ct);

    [HttpPut("items/{itemId:guid}/draft")]
    public Task<ShiftClosureDto> SaveDraft(
        Guid itemId, [FromBody] SaveShiftClosureDraftRequest request, CancellationToken ct) =>
        _service.SaveDraftAsync(itemId, request, Caller, ct);

    [HttpPost("items/{itemId:guid}/close")]
    public Task<ShiftClosureDto> Close(
        Guid itemId, [FromBody] CloseShiftClosureRequest request, CancellationToken ct) =>
        _service.CloseAsync(itemId, request, Caller, ct);

    [HttpGet("pending")]
    public Task<IReadOnlyList<ShiftClosurePendingDto>> Pending(CancellationToken ct) =>
        _service.GetPendingAsync(Caller, ct);

    [HttpPatch("{closureId:guid}/corrections")]
    [Authorize(Policy = "ManagerOrAdmin")]
    public async Task<IActionResult> Correct(
        Guid closureId, [FromBody] CorrectShiftClosureRequest request, CancellationToken ct)
    {
        await _service.CorrectAsync(closureId, request, Caller, ct);
        return NoContent();
    }

    [HttpGet("{closureId:guid}/corrections")]
    public Task<IReadOnlyList<ShiftClosureCorrectionDto>> Corrections(
        Guid closureId, CancellationToken ct) =>
        _service.GetCorrectionsAsync(closureId, Caller, ct);

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ShiftAccessException("Пользователь не идентифицирован");

    private StaffingCaller Caller => new(
        UserId,
        User.IsInRole(Roles.ShiftLead),
        User.IsInRole(Roles.Manager) || User.IsInRole(Roles.Admin));
}
