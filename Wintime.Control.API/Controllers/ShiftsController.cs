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

    public ShiftsController(IShiftService shiftService) => _shiftService = shiftService;

    /// <summary>
    /// Список смен (доступен всем авторизованным пользователям)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShiftDto>>> GetShifts()
    {
        return Ok(await _shiftService.GetShiftsAsync());
    }

    /// <summary>
    /// Сохранить расписание смен (полная замена списка, только Admin)
    /// </summary>
    [HttpPut]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<IEnumerable<ShiftDto>>> SaveShifts([FromBody] SaveShiftsRequestDto request)
    {
        if (request.Shifts is null || request.Shifts.Count == 0)
            return BadRequest(new { errors = new[] { "Список смен не может быть пустым" } });

        var errors = _shiftService.Validate(request.Shifts);
        if (errors.Count > 0)
            return BadRequest(new { errors });

        return Ok(await _shiftService.SaveShiftsAsync(request.Shifts));
    }
}
