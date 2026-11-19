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
    private readonly IShiftClosurePdfService _pdfService;

    public ShiftClosuresController(IShiftClosureService service, IShiftClosurePdfService pdfService)
    {
        _service = service;
        _pdfService = pdfService;
    }

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

    [HttpGet("items/{itemId:guid}/blank-pdf")]
    public async Task<IActionResult> BlankPdf(Guid itemId, CancellationToken ct)
    {
        var draft = await _service.GetAsync(itemId, Caller, ct);
        var bytes = _pdfService.RenderBlank(draft, DateTime.UtcNow);
        return File(bytes, "application/pdf",
            $"shift-closure-blank-{draft.ShiftDate:yyyy-MM-dd}-{itemId:N}.pdf");
    }

    [HttpGet("{closureId:guid}/pdf")]
    public async Task<IActionResult> ClosedPdf(Guid closureId, CancellationToken ct)
    {
        var closed = await _service.GetClosedAsync(closureId, Caller, ct);
        var bytes = _pdfService.RenderClosed(closed);
        return File(bytes, "application/pdf",
            $"shift-closure-{closed.ShiftDate:yyyy-MM-dd}-{closureId:N}.pdf");
    }

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
