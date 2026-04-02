using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Template;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TemplatesController : ControllerBase
{
    private readonly ControlDbContext _context;

    public TemplatesController(ControlDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Список шаблонов оборудования
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
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
            SensorCount = 0 // TODO: Посчитать из JsonConfig
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Получить детали шаблона
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
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
            SensorCount = 0
        };

        return Ok(dto);
    }

    /// <summary>
    /// Загрузить новый шаблон (JSON)
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

        var dto = new TemplateDto
        {
            Id = template.Id,
            Name = template.Name,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            Version = template.Version,
            Author = template.Author,
            CreatedAt = template.CreatedAt,
            SensorCount = 0
        };

        return CreatedAtAction(nameof(GetTemplateById), new { id = template.Id }, dto);
    }
}