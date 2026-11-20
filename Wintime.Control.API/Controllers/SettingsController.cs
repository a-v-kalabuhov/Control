using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Settings;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.API.Controllers;

/// <summary>Настройки приложения (ADR-0015): планирование, конфликты и безопасность.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ManagerOrAdmin")]
public class SettingsController : ControllerBase
{
    private readonly IPlanningDefaultsService _planningDefaults;
    private readonly ISecuritySettingsService _securitySettings;

    public SettingsController(IPlanningDefaultsService planningDefaults,
                              ISecuritySettingsService securitySettings)
    {
        _planningDefaults = planningDefaults;
        _securitySettings = securitySettings;
    }

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
            request.SetupMinutes, request.ReleaseBufferFixedMinutes, request.ReleaseBufferPercent, userId,
            request.ConflictThresholdMinutes);
        return Ok(ToDto(saved));
    }

    /// <summary>Настройки политики паролей.</summary>
    [HttpGet("security")]
    public async Task<ActionResult<SecuritySettingsDto>> GetSecuritySettings() =>
        Ok(ToDto(await _securitySettings.GetAsync()));

    /// <summary>Изменить политику для последующего создания пользователей и смены паролей.</summary>
    [HttpPut("security")]
    public async Task<ActionResult<SecuritySettingsDto>> UpdateSecuritySettings(
        [FromBody] UpdateSecuritySettingsRequestDto request)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var saved = await _securitySettings.UpdateAsync(request.AllowWeakPasswords, userId);
        return Ok(ToDto(saved));
    }

    private static PlanningDefaultsDto ToDto(PlanningDefaults d) => new()
    {
        SetupMinutes = d.SetupMinutes,
        ReleaseBufferFixedMinutes = d.ReleaseBufferFixedMinutes,
        ReleaseBufferPercent = d.ReleaseBufferPercent,
        ConflictThresholdMinutes = d.ConflictThresholdMinutes,
        UpdatedAt = d.UpdatedAt
    };

    private static SecuritySettingsDto ToDto(SecuritySettings s) => new()
    {
        AllowWeakPasswords = s.AllowWeakPasswords,
        UpdatedAt = s.UpdatedAt
    };
}
