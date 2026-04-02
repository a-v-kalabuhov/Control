using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImmController : ControllerBase
{
    private readonly ControlDbContext _context;

    public ImmController(ControlDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Список всех ТПА (IMM)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<IEnumerable<ImmDto>>> GetImmList([FromQuery] bool? isActive = null)
    {
        var query = _context.Imms
            .Include(i => i.Template)
            .AsQueryable();

        if (isActive.HasValue)
        {
            query = query.Where(i => i.IsActive == isActive.Value);
        }

        var imms = await query
            .Select(i => new ImmDto
            {
                Id = i.Id,
                Name = i.Name,
                InventoryNumber = i.InventoryNumber,
                TemplateId = i.TemplateId,
                Manufacturer = i.Template.Manufacturer,
                Model = i.Template.Model,
                IsActive = i.IsActive,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return Ok(imms);
    }

    /// <summary>
    /// Получить данные ТПА по ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<ImmDto>> GetImmById(Guid id)
    {
        var imm = await _context.Imms
            .Include(i => i.Template)
            .FirstOrDefaultAsync(i => i.Id == id);

        if (imm == null)
            return NotFound();

        var dto = new ImmDto
        {
            Id = imm.Id,
            Name = imm.Name,
            InventoryNumber = imm.InventoryNumber,
            TemplateId = imm.TemplateId,
            Manufacturer = imm.Template.Manufacturer,
            Model = imm.Template.Model,
            IsActive = imm.IsActive,
            CreatedAt = imm.CreatedAt
        };

        return Ok(dto);
    }

    /// <summary>
    /// Создать новый ТПА (IMM)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ImmDto>> CreateImm([FromBody] CreateImmRequestDto request)
    {
        var template = await _context.Templates.FindAsync(request.TemplateId);
        if (template == null)
            return BadRequest("Шаблон оборудования не найден");

        var imm = new Imm
        {
            Name = request.Name,
            InventoryNumber = request.InventoryNumber,
            TemplateId = request.TemplateId,
            IsActive = true
        };

        _context.Imms.Add(imm);
        await _context.SaveChangesAsync();

        var dto = new ImmDto
        {
            Id = imm.Id,
            Name = imm.Name,
            InventoryNumber = imm.InventoryNumber,
            TemplateId = imm.TemplateId,
            Manufacturer = template.Manufacturer,
            Model = template.Model,
            IsActive = imm.IsActive,
            CreatedAt = imm.CreatedAt
        };

        return CreatedAtAction(nameof(GetImmById), new { id = imm.Id }, dto);
    }

    /// <summary>
    /// Обновить данные ТПА
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdateImm(Guid id, [FromBody] UpdateImmRequestDto request)
    {
        var imm = await _context.Imms.FindAsync(id);
        if (imm == null)
            return NotFound();

        if (request.Name != null)
            imm.Name = request.Name;
        if (request.InventoryNumber != null)
            imm.InventoryNumber = request.InventoryNumber;
        if (request.IsActive.HasValue)
            imm.IsActive = request.IsActive.Value;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Текущий статус ТПА (для дашборда)
    /// </summary>
    [HttpGet("{id:guid}/status")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer}")]
    public async Task<ActionResult<ImmStatusDto>> GetImmStatus(Guid id)
    {
        var imm = await _context.Imms.FindAsync(id);
        if (imm == null)
            return NotFound();

        // Получаем текущее активное задание
        var currentTask = await _context.Tasks
            .FirstOrDefaultAsync(t => t.ImmId == id && t.Status == Core.Enums.TaskStatus.InProgress);

        // Получаем последний статус из телеметрии
        var lastTelemetry = await _context.Telemetry
            .Where(t => t.ImmId == id && t.ParameterName == "status")
            .OrderByDescending(t => t.Timestamp)
            .FirstOrDefaultAsync();

        var status = new ImmStatusDto
        {
            ImmId = imm.Id,
            Status = lastTelemetry?.ValueText ?? "Offline",
            CurrentTaskId = currentTask?.Id,
            CurrentMoldId = currentTask?.MoldId,
            CurrentCycleTime = 0, // TODO: Вычислить из телеметрии
            LastUpdate = lastTelemetry?.Timestamp ?? DateTime.MinValue
        };

        return Ok(status);
    }

    /// <summary>
    /// История телеметрии ТПА
    /// </summary>
    [HttpGet("{id:guid}/telemetry")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<TelemetryDto>>> GetImmTelemetry(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] List<string>? parameters = null)
    {
        var query = _context.Telemetry
            .Where(t => t.ImmId == id && t.Timestamp >= from && t.Timestamp <= to)
            .AsQueryable();

        if (parameters != null && parameters.Any())
        {
            query = query.Where(t => parameters.Contains(t.ParameterName));
        }

        var telemetry = await query
            .OrderBy(t => t.Timestamp)
            .Select(t => new TelemetryDto
            {
                Timestamp = t.Timestamp,
                ParameterName = t.ParameterName,
                ValueNumeric = t.ValueNumeric,
                ValueText = t.ValueText
            })
            .ToListAsync();

        return Ok(telemetry);
    }

    /// <summary>
    /// Получить статистику по циклам за период
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<ImmStatisticsDto>> GetImmStatistics(
        Guid id,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? taskId = null)
    {
        // TODO: Реализовать агрегацию данных из событий и телеметрии
        var statistics = new ImmStatisticsDto
        {
            ImmId = id,
            PeriodFrom = from,
            PeriodTo = to,
            TotalCycles = 0,
            CyclesByTask = new List<TaskCycleStatsDto>(),
            CyclesInSetup = 0,
            CyclesInAlarm = 0,
            AvgCycleTime = 0
        };

        return Ok(statistics);
    }
}