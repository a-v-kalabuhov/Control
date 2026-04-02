using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.Admin;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminController : ControllerBase
{
    // TODO: Inject Settings Service

    /// <summary>
    /// Получить настройки системы
    /// </summary>
    [HttpGet("settings")]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<ActionResult<SystemSettingsDto>> GetSystemSettings()
    {
        // TODO: Загрузить из конфигурации / БД
        var settings = new SystemSettingsDto
        {
            MqttBrokerUrl = "tcp://localhost:1883",
            MqttPort = 1883,
            SessionTimeoutMinutes = 60,
            TelemetryIntervalSeconds = 5
        };

        return Ok(settings);
    }

    /// <summary>
    /// Обновить настройки системы
    /// </summary>
    [HttpPut("settings")]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<IActionResult> UpdateSystemSettings([FromBody] UpdateSystemSettingsRequestDto request)
    {
        // TODO: Сохранить в БД / конфигурацию
        return NoContent();
    }

    /// <summary>
    /// Проверить подключение к MQTT-брокеру
    /// </summary>
    [HttpPost("settings/test-mqtt")]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<IActionResult> TestMqttConnection([FromBody] MqttTestRequestDto request)
    {
        // TODO: Реализовать тест подключения через MQTTnet
        return Ok(new { message = "Подключение успешно" });
    }
}