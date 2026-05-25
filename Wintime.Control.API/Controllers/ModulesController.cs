using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.SDK;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = Roles.Admin)]
public class ModulesController(
    ControlDbContext db,
    IReadOnlyList<IAppModule> loadedModules) : ControllerBase
{
    /// <summary>
    /// Список зарегистрированных модулей и статус загрузки.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModuleStatusDto>>> GetModules()
    {
        var records = await db.AppModules.ToListAsync();

        var result = records.Select(r =>
        {
            var loaded = loadedModules.FirstOrDefault(m => m.Key == r.Key);
            return new ModuleStatusDto
            {
                Key = r.Key,
                IsEnabled = r.IsEnabled,
                IsLoaded = loaded is not null,
                InstalledVersion = r.InstalledVersion,
                DisplayName = loaded?.DisplayName,
                ModuleVersion = loaded?.ModuleVersion?.ToString(),
                EnabledAt = r.EnabledAt,
                DisabledAt = r.DisabledAt
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Включить модуль (требует перезапуска для загрузки DLL).
    /// </summary>
    [HttpPost("{key}/enable")]
    public async Task<IActionResult> Enable(string key)
    {
        var record = await db.AppModules.FindAsync(key);
        if (record is null)
            return NotFound($"Module '{key}' not found in registry.");

        record.IsEnabled = true;
        record.EnabledAt = DateTime.UtcNow;
        record.DisabledAt = null;
        await db.SaveChangesAsync();

        return Ok(new { requiresRestart = !loadedModules.Any(m => m.Key == key) });
    }

    /// <summary>
    /// Отключить модуль.
    /// </summary>
    [HttpPost("{key}/disable")]
    public async Task<IActionResult> Disable(string key, [FromQuery] bool retainData = true)
    {
        var record = await db.AppModules.FindAsync(key);
        if (record is null)
            return NotFound($"Module '{key}' not found in registry.");

        record.IsEnabled = false;
        record.DisabledAt = DateTime.UtcNow;
        record.RetainDataOnDisable = retainData;
        await db.SaveChangesAsync();

        return Ok(new { requiresRestart = true });
    }

    /// <summary>
    /// Зарегистрировать модуль в реестре (без загрузки DLL — требует перезапуска).
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterModuleRequest request)
    {
        if (await db.AppModules.AnyAsync(m => m.Key == request.Key))
            return Conflict($"Module '{request.Key}' is already registered.");

        db.AppModules.Add(new AppModuleRecord
        {
            Key = request.Key,
            IsEnabled = true,
            InstalledVersion = request.Version,
            EnabledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return Ok(new { requiresRestart = true });
    }
}

public record ModuleStatusDto
{
    public string Key { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsLoaded { get; init; }
    public string? InstalledVersion { get; init; }
    public string? DisplayName { get; init; }
    public string? ModuleVersion { get; init; }
    public DateTime? EnabledAt { get; init; }
    public DateTime? DisabledAt { get; init; }
}

public record RegisterModuleRequest(string Key, string? Version);
