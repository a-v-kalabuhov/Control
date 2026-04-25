using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Emulator.Models;
using Wintime.Control.Emulator.Services;

namespace Wintime.Control.Emulator.Controllers;

/// <summary>
/// Emulator API
/// </summary>

[ApiController]
[Route("api/[controller]")]
public class EmulatorController : ControllerBase
{
    private readonly EmulationOrchestrator _orchestrator;

    public EmulatorController(EmulationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Starts a new instance of IMM emulation.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    [HttpPost("instances")]
    public async Task<IActionResult> Start([FromBody] EmulationRequest request)
    {
        await _orchestrator.StartAsync(request.ImmId, request);
        return Accepted(); // 202 - emulation request accepted and processing
    }

    /// <summary>
    /// Eliminates the instance of IMM emulation.
    /// </summary>
    /// <param name="immId">Neseccary IMM emulation instance ID</param>
    [HttpDelete("instances/{immId}")]
    public async Task<IActionResult> Stop(string immId)
    {
        await _orchestrator.StopAsync(immId);
        return NoContent(); // 204 - successfully stopped
    }

    /// <summary>
    /// Returns list of all emulation instances with ID, status and emulation start time.
    /// </summary>
    [HttpGet("instances")]
    public IActionResult GetStatuses()
    {
        return Ok(_orchestrator.GetStatuses());
    }
}