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
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // 401 — Ошибка авторизации
            _logger.LogError(ex, "Authentication failed: invalid credentials or token expired");
            return StatusCode(401, new { 
                code = "AUTH_FAILED",
                message = "Ошибка авторизации. Проверьте логин/пароль в настройках эмулятора.",
                details = ex.Message 
            });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // 403 — Нет прав доступа
            _logger.LogError(ex, "Access forbidden: insufficient permissions");
            return StatusCode(403, new { 
                code = "ACCESS_DENIED",
                message = "Нет прав доступа. Убедитесь, что у пользователя есть роль для чтения ТПА.",
                details = ex.Message 
            });
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            // Таймаут подключения
            _logger.LogError(ex, "Main API timeout");
            return StatusCode(504, new { 
                code = "API_TIMEOUT",
                message = "Основной API не отвечает. Проверьте, запущен ли сервис и сетевое подключение.",
                details = "Превышено время ожидания ответа" 
            });
        }
        catch (HttpRequestException ex)
        {
            // Другие HTTP-ошибки (503, 502, и т.д.)
            _logger.LogError(ex, "Main API unavailable");
            return StatusCode(502, new { 
                code = "API_UNAVAILABLE",
                message = "Основной API недоступен. Проверьте, запущен ли сервис.",
                details = ex.Message 
            });
        }
        catch (Exception ex)
        {
            // Неожиданные ошибки
            _logger.LogError(ex, "Unexpected error fetching IMMs");
            return StatusCode(500, new { 
                code = "INTERNAL_ERROR",
                message = "Не удалось получить список ТПА.",
                details = ex.Message 
            });
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
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return StatusCode(401, new { 
                code = "AUTH_FAILED",
                message = "Ошибка авторизации при загрузке шаблона.",
                details = ex.Message 
            });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { 
                code = "API_UNAVAILABLE",
                message = "Не удалось загрузить шаблон. Основной API недоступен.",
                details = ex.Message 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { 
                code = "INTERNAL_ERROR",
                message = "Внутренняя ошибка при загрузке шаблона.",
                details = ex.Message 
            });
        }
    }
}