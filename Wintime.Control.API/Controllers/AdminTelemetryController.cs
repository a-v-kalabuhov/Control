using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Telemetry;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/admin/telemetry")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminTelemetryController : ControllerBase
{
    private readonly ITelemetryAdministrationService _service;

    public AdminTelemetryController(ITelemetryAdministrationService service) => _service = service;

    [HttpGet("statistics")]
    public async Task<ActionResult<TelemetryStatisticsDto>> GetStatistics(
        [FromQuery] string? timeZone, CancellationToken ct) =>
        Ok(await _service.GetStatisticsAsync(timeZone, ct));

    [HttpGet("cleanup/preview")]
    public async Task<ActionResult<TelemetryCleanupPreviewDto>> PreviewCleanup(
        [FromQuery] DateOnly date, [FromQuery] string? timeZone, CancellationToken ct) =>
        Ok(await _service.PreviewCleanupAsync(date, timeZone, ct));

    [HttpDelete("cleanup")]
    public async Task<ActionResult<TelemetryCleanupResultDto>> Cleanup(
        [FromQuery] DateOnly date, [FromQuery] string? timeZone, CancellationToken ct)
    {
        var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        return Ok(await _service.CleanupAsync(date, timeZone, adminUserId, ct));
    }
}
