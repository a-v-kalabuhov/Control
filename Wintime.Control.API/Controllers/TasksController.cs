using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Task;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ControlDbContext _context;

    public TasksController(ControlDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Список заданий (с фильтрацией)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<TaskDto>>> GetTaskList(
        [FromQuery] Core.Enums.TaskStatus? status = null,
        [FromQuery] Guid? immId = null,
        [FromQuery] string? personnelId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null)
    {
        var query = _context.Tasks
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (immId.HasValue)
            query = query.Where(t => t.ImmId == immId.Value);
        if (!string.IsNullOrEmpty(personnelId))
            query = query.Where(t => t.PersonnelId == personnelId);
        if (dateFrom.HasValue)
            query = query.Where(t => t.IssuedAt >= dateFrom.Value);
        if (dateTo.HasValue)
            query = query.Where(t => t.IssuedAt <= dateTo.Value);

        var tasks = await query.ToListAsync();

        var dtos = tasks.Select(t => new TaskDto
        {
            Id = t.Id,
            ImmId = t.ImmId,
            ImmName = t.Imm.Name,
            MoldId = t.MoldId,
            MoldName = t.Mold.Name,
            PersonnelId = t.PersonnelId,
            PersonnelName = t.Personnel?.FullName,
            PlanQuantity = t.PlanQuantity,
            ActualQuantity = t.ActualQuantity,
            ProgressPercent = t.PlanQuantity > 0 ? (decimal)t.ActualQuantity / t.PlanQuantity * 100 : 0,
            Status = t.Status,
            IssuedAt = t.IssuedAt,
            StartedAt = t.StartedAt,
            CompletedAt = t.CompletedAt,
            ClosedAt = t.ClosedAt,
            CloseReason = t.CloseReason,
            Note = t.Note
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Получить детали задания
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<TaskDto>> GetTaskById(Guid id)
    {
        var task = await _context.Tasks
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null)
            return NotFound();

        var dto = new TaskDto
        {
            Id = task.Id,
            ImmId = task.ImmId,
            ImmName = task.Imm.Name,
            MoldId = task.MoldId,
            MoldName = task.Mold.Name,
            PersonnelId = task.PersonnelId,
            PersonnelName = task.Personnel?.FullName,
            PlanQuantity = task.PlanQuantity,
            ActualQuantity = task.ActualQuantity,
            ProgressPercent = task.PlanQuantity > 0 ? (decimal)task.ActualQuantity / task.PlanQuantity * 100 : 0,
            Status = task.Status,
            IssuedAt = task.IssuedAt,
            StartedAt = task.StartedAt,
            CompletedAt = task.CompletedAt,
            ClosedAt = task.ClosedAt,
            CloseReason = task.CloseReason,
            Note = task.Note
        };

        return Ok(dto);
    }

    /// <summary>
    /// Создать новое задание (ССЗ)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<TaskDto>> CreateTask([FromBody] CreateTaskRequestDto request)
    {
        var task = new Core.Entities.Task
        {
            ImmId = request.ImmId,
            MoldId = request.MoldId,
            PersonnelId = request.PersonnelId,
            PlanQuantity = request.PlanQuantity,
            Note = request.Note,
            Status = Core.Enums.TaskStatus.Draft,
            IssuedAt = DateTime.UtcNow
        };

        _context.Tasks.Add(task);
        await _context.SaveChangesAsync();

        var dto = new TaskDto
        {
            Id = task.Id,
            ImmId = task.ImmId,
            MoldId = task.MoldId,
            PersonnelId = task.PersonnelId,
            PlanQuantity = task.PlanQuantity,
            ActualQuantity = task.ActualQuantity,
            ProgressPercent = 0,
            Status = task.Status,
            IssuedAt = task.IssuedAt,
            Note = task.Note
        };

        return CreatedAtAction(nameof(GetTaskById), new { id = task.Id }, dto);
    }

    /// <summary>
    /// Обновить задание
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdateTask(Guid id, [FromBody] UpdateTaskRequestDto request)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (request.PlanQuantity.HasValue)
            task.PlanQuantity = request.PlanQuantity.Value;
        if (request.Note != null)
            task.Note = request.Note;
        if (request.Status.HasValue)
            task.Status = request.Status.Value;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Начать выполнение задания (сканирование QR)
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = $"{Roles.Adjuster}")]
    public async Task<IActionResult> StartTask(Guid id, [FromBody] StartTaskRequestDto request)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (task.Status != Core.Enums.TaskStatus.Issued && task.Status != Core.Enums.TaskStatus.Draft)
            return BadRequest("Задание уже в работе или завершено");

        // TODO: Валидировать QR-коды (совпадают ли с заданием)
        
        task.Status = Core.Enums.TaskStatus.InProgress;
        task.StartedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Задание начато" });
    }

    /// <summary>
    /// Завершить задание
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager}")]
    public async Task<IActionResult> CompleteTask(Guid id, [FromBody] CompleteTaskRequestDto request)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (task.Status != Core.Enums.TaskStatus.InProgress)
            return BadRequest("Задание не в работе");

        if (request.ActualQuantity.HasValue)
            task.ActualQuantity = request.ActualQuantity.Value;
        
        if (task.ActualQuantity < task.PlanQuantity && request.CompletionReason != null)
            task.CloseReason = request.CompletionReason;

        task.Status = Core.Enums.TaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Задание завершено" });
    }

    /// <summary>
    /// Закрыть задание (ручное закрытие в конце дня)
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = $"{Roles.Manager},{Roles.Admin}")]
    public async Task<IActionResult> CloseTask(Guid id, [FromBody] CloseTaskRequestDto? request = null)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (request?.CloseReason != null)
            task.CloseReason = request.CloseReason;

        task.Status = Core.Enums.TaskStatus.Closed;
        task.ClosedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Задание закрыто" });
    }
}