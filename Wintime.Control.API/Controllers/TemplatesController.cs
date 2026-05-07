using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Template;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TemplatesController : ControllerBase
{
    private readonly ControlDbContext _context;
    private readonly ITemplateCache _templateCache;

    public TemplatesController(ControlDbContext context, ITemplateCache templateCache)
    {
        _context = context;
        _templateCache = templateCache;
    }

    /// <summary>
    /// Список шаблонов оборудования
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Emulator}")]
    public async Task<ActionResult<IEnumerable<TemplateDto>>> GetTemplates()
    {
        var templates = await _context.Templates.ToListAsync();

        var dtos = templates.Select(t => new TemplateDto
        {
            Id = t.Id,
            Name = t.Name,
            Manufacturer = t.Manufacturer,
            Model = t.Model,
            Version = t.Version,
            Author = t.Author,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            JsonConfig = t.JsonConfig,
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Получить детали шаблона
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Emulator}")]
    public async Task<ActionResult<TemplateDto>> GetTemplateById(Guid id)
    {
        var template = await _context.Templates.FindAsync(id);
        if (template == null)
            return NotFound();

        var dto = new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            Version = template.Version,
            Author = template.Author,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            JsonConfig = template.JsonConfig,
        };

        return Ok(dto);
    }

    /// <summary>
    /// Обновить шаблон (JSON)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<ActionResult> UpdateTemplate(Guid id, [FromBody] CreateTemplateRequestDto request)
    {
        var template = await _context.Templates.FindAsync(id);
        if (template == null)
            return NotFound();

        template.Name = request.Name;
        template.JsonConfig = request.JsonConfig?.ToString() ?? string.Empty;
        template.Manufacturer = request.Manufacturer ?? string.Empty;
        template.Model = request.Model ?? string.Empty;
        template.Version = request.Version ?? string.Empty;
        template.Author = request.Author ?? string.Empty;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }

        _templateCache.Upsert(template);

        return NoContent();
    }

    /// <summary>
    /// Создать новый шаблон (JSON)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<ActionResult<TemplateDto>> CreateTemplate([FromBody] CreateTemplateRequestDto request)
    {
        var template = new Template
        {
            Name = request.Name,
            Manufacturer = request.Manufacturer ?? string.Empty,
            Model = request.Model ?? string.Empty,
            Version = request.Version ?? "1.0",
            Author = request.Author ?? string.Empty,
            JsonConfig = System.Text.Json.JsonSerializer.Serialize(request.JsonConfig),
            IsActive = true
        };

        _context.Templates.Add(template);
        await _context.SaveChangesAsync();

        _templateCache.Upsert(template);

        var dto = new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            Version = template.Version,
            Author = template.Author,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            JsonConfig = template.JsonConfig,
        };

        return CreatedAtAction(nameof(GetTemplateById), new { id = template.Id }, dto);
    }
}
