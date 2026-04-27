using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Emulator.Models;
using Wintime.Control.Emulator.Services;

namespace Wintime.Control.Emulator.Controllers;

/// <summary>
/// Прокси-контроллер для запросов к основному API (Wintime.Control.API)
/// </summary>
[ApiController]
[Route("api/main")]
public class MainApiController : ControllerBase
{
    private readonly IImmApiClient _immApiClient;
    private readonly ILogger<MainApiController> _logger;

    public MainApiController(IImmApiClient immApiClient, ILogger<MainApiController> logger)
    {
        _immApiClient = immApiClient;
        _logger = logger;
    }

    /// <summary>
    /// Получить список зарегистрированных ТПА из основного API
    /// </summary>
    [HttpGet("imm")]
    public async Task<ActionResult<List<ImmDto>>> GetImms(CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Fetching IMMs from main API");
            var imms = await _immApiClient.GetImmsAsync(ct);
            
            // Фильтруем только активные ТПА (опционально)
            var activeImms = imms.Where(i => i.IsActive).ToList();
            
            _logger.LogInformation("Retrieved {Count} active IMMs from main API", activeImms.Count);
            return Ok(activeImms);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch IMMs from main API");
            return StatusCode(502, new { message = "Не удалось получить список ТПА из основного API", error = ex.Message });
        }
    }

    /// <summary>
    /// Получить шаблон сенсоров для ТПА
    /// </summary>
    [HttpGet("templates/{id}")]
    public async Task<ActionResult<TemplateDto>> GetTemplate(string id, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Fetching template {TemplateId} from main API", id);
            var template = await _immApiClient.GetTemplateAsync(id, ct);
            return Ok(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch template {TemplateId}", id);
            return StatusCode(502, new { message = "Не удалось получить шаблон", error = ex.Message });
        }
    }
}