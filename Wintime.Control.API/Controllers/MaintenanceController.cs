using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/admin/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class MaintenanceController(ControlDbContext db) : ControllerBase
{
    private const string MaintenanceModeKey = "MaintenanceModeActive";

    /// <summary>
    /// Статус режима обслуживания.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<MaintenanceStatusDto>> GetStatus()
    {
        var entry = await db.SystemConfig.FindAsync(MaintenanceModeKey);
        var isActive = entry?.Value == "true";
        return Ok(new MaintenanceStatusDto(isActive));
    }

    /// <summary>
    /// Войти в режим обслуживания (пользователи получат 503).
    /// </summary>
    [HttpPost("enter")]
    public async Task<IActionResult> Enter()
    {
        await UpsertConfig(MaintenanceModeKey, "true");
        return Ok(new MaintenanceStatusDto(true));
    }

    /// <summary>
    /// Выйти из режима обслуживания.
    /// </summary>
    [HttpPost("exit")]
    public async Task<IActionResult> Exit()
    {
        await UpsertConfig(MaintenanceModeKey, "false");
        return Ok(new MaintenanceStatusDto(false));
    }

    /// <summary>
    /// Применить pending EF-миграции.
    /// </summary>
    [HttpPost("migrate")]
    public async Task<IActionResult> Migrate()
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        await db.Database.MigrateAsync();
        return Ok(new { appliedMigrations = pending });
    }

    /// <summary>
    /// Перезапустить приложение (systemd/IIS перезапустит процесс).
    /// </summary>
    [HttpPost("restart")]
    public IActionResult Restart()
    {
        // Завершаемся через секунду, чтобы ответ успел вернуться клиенту
        Task.Delay(1000).ContinueWith(_ => Environment.Exit(0));
        return Ok(new { message = "Restarting..." });
    }

    private async Task UpsertConfig(string key, string value)
    {
        var entry = await db.SystemConfig.FindAsync(key);
        if (entry is null)
        {
            db.SystemConfig.Add(new Core.Entities.SystemConfigEntry
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            entry.Value = value;
            entry.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}

public record MaintenanceStatusDto(bool IsActive);
