using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Settings;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.API.Controllers;

/// <summary>Настройки приложения (ADR-0015). Сейчас — только умолчания планирования (PZP-14).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ManagerOrAdmin")]
public class SettingsController : ControllerBase
{
    private readonly IPlanningDefaultsService _planningDefaults;

    public SettingsController(IPlanningDefaultsService planningDefaults) => _planningDefaults = planningDefaults;

    /// <summary>Умолчания норматива наладки и буфера выпуска для новых заданий.</summary>
    [HttpGet("planning-defaults")]
    public async Task<ActionResult<PlanningDefaultsDto>> GetPlanningDefaults() =>
        Ok(ToDto(await _planningDefaults.GetAsync()));

    /// <summary>Изменить умолчания. Существующие задания не меняются.</summary>
    [HttpPut("planning-defaults")]
    public async Task<ActionResult<PlanningDefaultsDto>> UpdatePlanningDefaults(
        [FromBody] UpdatePlanningDefaultsRequestDto request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var saved = await _planningDefaults.UpdateAsync(
            request.SetupMinutes, request.ReleaseBufferFixedMinutes, request.ReleaseBufferPercent, userId);
        return Ok(ToDto(saved));
    }

    private static PlanningDefaultsDto ToDto(PlanningDefaults d) => new()
    {
        SetupMinutes = d.SetupMinutes,
        ReleaseBufferFixedMinutes = d.ReleaseBufferFixedMinutes,
        ReleaseBufferPercent = d.ReleaseBufferPercent,
        UpdatedAt = d.UpdatedAt
    };
}
