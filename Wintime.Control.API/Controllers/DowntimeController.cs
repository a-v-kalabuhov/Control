using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Downtime;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DowntimeController : ControllerBase
{
    private readonly ControlDbContext _context;

    public DowntimeController(ControlDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Справочник причин простоев
    /// </summary>
    [HttpGet("reasons")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<DowntimeReasonDto>>> GetDowntimeReasons(
        [FromQuery] bool? isActive = null,
        [FromQuery] string? type = null)
    {
        var query = _context.DowntimeReasons.AsQueryable();

        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(type))
            query = query.Where(r => r.Type == type);

        var reasons = await query.ToListAsync();

        var dtos = reasons.Select(r => new DowntimeReasonDto
        {
            Id = r.Id,
            Name = r.Name,
            Type = r.Type,
            IsActive = r.IsActive
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Добавить причину простоя
    /// </summary>
    [HttpPost("reasons")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<DowntimeReasonDto>> CreateDowntimeReason([FromBody] CreateDowntimeReasonRequestDto request)
    {
        var reason = new DowntimeReason
        {
            Name = request.Name,
            Type = request.Type,
            IsActive = true
        };

        _context.DowntimeReasons.Add(reason);
        await _context.SaveChangesAsync();

        var dto = new DowntimeReasonDto
        {
            Id = reason.Id,
            Name = reason.Name,
            Type = reason.Type,
            IsActive = reason.IsActive
        };

        return CreatedAtAction(nameof(GetDowntimeReasons), new { }, dto);
    }

    /// <summary>
    /// Начать простой (вручную)
    /// </summary>
    [HttpPost("events/downtime/start")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager}")]
    public async Task<IActionResult> StartDowntime([FromBody] StartDowntimeRequestDto request)
    {
        var imm = await _context.Imms.FindAsync(request.ImmId);
        if (imm == null)
            return NotFound("ТПА не найден");

        var reason = await _context.DowntimeReasons.FindAsync(request.ReasonId);
        if (reason == null)
            return NotFound("Причина простоя не найдена");

        var evt = new Event
        {
            ImmId = request.ImmId,
            EventType = Core.Enums.EventType.Downtime,
            ReasonId = request.ReasonId,
            ReasonName = reason.Name,
            StartTime = request.StartTime ?? DateTime.UtcNow,
            PersonnelId = request.PersonnelId
        };

        _context.Events.Add(evt);
        await _context.SaveChangesAsync();

        return Ok(new { eventId = evt.Id, message = "Простой начат" });
    }

    /// <summary>
    /// Завершить простой
    /// </summary>
    [HttpPost("events/downtime/stop")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager}")]
    public async Task<IActionResult> StopDowntime([FromBody] StopDowntimeRequestDto request)
    {
        var evt = await _context.Events
            .Where(e => e.ImmId == request.ImmId && e.EndTime == null)
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefaultAsync();

        if (evt == null)
            return NotFound("Активный простой не найден");

        evt.EndTime = request.EndTime ?? DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Простой завершён" });
    }

    /// <summary>
    /// История событий (аварии, простои, наладки)
    /// </summary>
    [HttpGet("events")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<EventDto>>> GetEvents(
        [FromQuery] Guid? immId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var query = _context.Events
            .Include(e => e.Imm)
            .Include(e => e.Reason)
            .AsQueryable();

        if (immId.HasValue)
            query = query.Where(e => e.ImmId == immId.Value);
        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(e => e.EventType.ToString() == eventType);
        if (from.HasValue)
            query = query.Where(e => e.StartTime >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.StartTime <= to.Value);

        var events = await query.ToListAsync();

        var dtos = events.Select(e => new EventDto
        {
            Id = e.Id,
            ImmId = e.ImmId,
            EventType = e.EventType.ToString(),
            ReasonId = e.ReasonId,
            ReasonName = e.ReasonName,
            ErrorCode = e.ErrorCode,
            ErrorMessage = e.ErrorMessage,
            StartTime = e.StartTime,
            EndTime = e.EndTime,
            DurationSeconds = e.DurationSeconds,
            PersonnelId = e.PersonnelId
        }).ToList();

        return Ok(dtos);
    }
}