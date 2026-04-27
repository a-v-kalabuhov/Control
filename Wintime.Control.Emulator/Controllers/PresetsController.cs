namespace Wintime.Control.Emulator.Controllers;

using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Emulator.Models;
using Wintime.Control.Emulator.Services;

/// <summary>
/// Контроллер пресетов.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PresetsController : ControllerBase
{
    private readonly IPresetStorage _storage;

    public PresetsController(IPresetStorage storage)
    {
        _storage = storage;
    }

    [HttpGet("{immId}")]
    public async Task<ActionResult<EmulationPreset>> GetPreset(string immId, CancellationToken ct)
    {
        var preset = await _storage.LoadAsync(immId, ct);
        if (preset == null)
            return NotFound();
        return Ok(preset);
    }

    [HttpPost("{immId}")]
    public async Task<IActionResult> SavePreset(string immId, [FromBody] EmulationPreset preset, CancellationToken ct)
    {
        if (preset.ImmId != immId)
            return BadRequest("ImmId mismatch");
        
        await _storage.SaveAsync(preset, ct);
        return NoContent();
    }

    [HttpDelete("{immId}")]
    public async Task<IActionResult> DeletePreset(string immId, CancellationToken ct)
    {
        await _storage.DeleteAsync(immId, ct);
        return NoContent();
    }

    [HttpGet("list")]
    public async Task<ActionResult<List<string>>> ListPresets(CancellationToken ct)
    {
        var ids = await _storage.ListAsync(ct);
        return Ok(ids);
    }
}