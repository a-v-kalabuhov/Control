using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShiftsController : ControllerBase
{
    private readonly IShiftService _shiftService;
    private readonly IShiftDefaultLeadService _defaultLeads;

    public ShiftsController(IShiftService shiftService, IShiftDefaultLeadService defaultLeads)
    {
        _shiftService = shiftService;
        _defaultLeads = defaultLeads;
    }

    /// <summary>
    /// Список смен (доступен всем авторизованным пользователям)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShiftDto>>> GetShifts()
    {
        return Ok(await _shiftService.GetShiftsAsync());
    }

    /// <summary>
    /// Сохранить расписание смен (полная замена списка, Admin и Manager)
    /// </summary>
    [HttpPut]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<ShiftDto>>> SaveShifts([FromBody] SaveShiftsRequestDto request)
    {
        if (request.Shifts is null || request.Shifts.Count == 0)
            return BadRequest(new { errors = new[] { "Список смен не может быть пустым" } });

        var errors = _shiftService.Validate(request.Shifts);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        return Ok(await _shiftService.SaveShiftsAsync(request.Shifts));
    }

    [HttpGet("default-leads")]
    [Authorize(Policy = "ManagerOrAdmin")]
    public Task<ShiftDefaultLeadSettingsDto> GetDefaultLeads(CancellationToken ct) =>
        _defaultLeads.GetAsync(ct);

    [HttpPut("default-leads")]
    [Authorize(Policy = "ManagerOrAdmin")]
    public Task<ShiftDefaultLeadSettingsDto> SaveDefaultLeads(
        [FromBody] SaveShiftDefaultLeadsRequestDto request, CancellationToken ct) =>
        _defaultLeads.SaveAsync(request, ct);
}
