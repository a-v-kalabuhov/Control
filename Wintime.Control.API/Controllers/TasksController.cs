using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly ControlDbContext _context;
    private readonly IEmulatorControlService _emulator;

    public TasksController(ControlDbContext context, IEmulatorControlService emulator)
    {
        _context = context;
        _emulator = emulator;
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
        var query = _context.ShiftTasks
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        else
            query = query.Where(t => t.Status != Core.Enums.TaskStatus.Completed
                                  && t.Status != Core.Enums.TaskStatus.Closed);
        if (immId.HasValue)
            query = query.Where(t => t.ImmId == immId.Value);
        if (!string.IsNullOrEmpty(personnelId))
            query = query.Where(t => t.PersonnelId == personnelId);
        if (dateFrom.HasValue)
            query = query.Where(t => t.IssuedAt >= dateFrom.Value);
        if (dateTo.HasValue)
            query = query.Where(t => t.IssuedAt <= dateTo.Value);

        var tasks = await query.ToListAsync();

        var dtos = tasks.Select(t => t.ToDto()).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Мои задания — для текущего наладчика (по JWT), разложенные по разделам планшетного интерфейса.
    /// </summary>
    /// <param name="boundary">
    /// Граница текущей смены (ISO 8601, UTC). Задания, выданные в этот момент и позже, относятся
    /// к текущей смене; выданные раньше — к прошедшим. Фронтенд вычисляет границу из расписания смен.
    /// Если не задана — используется текущий момент сервера.
    /// </param>
    /// <param name="search">Фильтр по названию ТПА или пресс-формы (регистронезависимый).</param>
    /// <param name="archivePage">Номер страницы архива (с 1).</param>
    /// <param name="archivePageSize">Размер страницы архива.</param>
    [HttpGet("my")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager},{Roles.Admin}")]
    public async Task<ActionResult<MyTasksDto>> GetMyTasks(
        [FromQuery] string? boundary = null,
        [FromQuery] string? search = null,
        [FromQuery] int archivePage = 1,
        [FromQuery] int archivePageSize = 20)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        // Граница смены приходит как UTC ISO ('…Z'); парсим инстант, сохраняя Kind=Utc для Npgsql.
        var boundaryUtc = DateTimeOffset.TryParse(
            boundary, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.UtcNow;

        if (archivePage < 1) archivePage = 1;
        if (archivePageSize < 1) archivePageSize = 20;

        var baseQuery = _context.ShiftTasks
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .Where(t => t.PersonnelId == userId)
            .Where(t => t.Status != Core.Enums.TaskStatus.Draft);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            baseQuery = baseQuery.Where(t =>
                EF.Functions.ILike(t.Imm.Name, pattern) ||
                EF.Functions.ILike(t.Mold.Name, pattern));
        }

        // Текущая смена: выдано на границе смены и позже (любой статус).
        // null IssuedAt быть не должно у не-черновиков, но на всякий случай относим к текущей смене,
        // чтобы такое задание не потерялось ни в одном разделе.
        var currentShift = await baseQuery
            .Where(t => t.IssuedAt == null || t.IssuedAt >= boundaryUtc)
            .OrderByDescending(t => t.IssuedAt)
            .ToListAsync();

        var pastQuery = baseQuery.Where(t => t.IssuedAt != null && t.IssuedAt < boundaryUtc);

        // Незавершённые с прошедших смен — оставшаяся работа, оформляем по возрастанию давности.
        var unfinished = await pastQuery
            .Where(t => t.Status == Core.Enums.TaskStatus.Issued
                     || t.Status == Core.Enums.TaskStatus.Setup
                     || t.Status == Core.Enums.TaskStatus.InProgress)
            .OrderBy(t => t.IssuedAt)
            .ToListAsync();

        // Архив — завершённые/закрытые задания прошедших смен, с пагинацией.
        var archiveQuery = pastQuery.Where(t => t.Status == Core.Enums.TaskStatus.Completed
                                             || t.Status == Core.Enums.TaskStatus.Closed);

        var archiveTotal = await archiveQuery.CountAsync();
        var archiveItems = await archiveQuery
            .OrderByDescending(t => t.IssuedAt)
            .Skip((archivePage - 1) * archivePageSize)
            .Take(archivePageSize)
            .ToListAsync();

        return Ok(new MyTasksDto
        {
            CurrentShift = currentShift.Select(t => t.ToDto()).ToList(),
            Unfinished = unfinished.Select(t => t.ToDto()).ToList(),
            Archive = new PagedTasksDto
            {
                Items = archiveItems.Select(t => t.ToDto()).ToList(),
                Total = archiveTotal,
                Page = archivePage,
                PageSize = archivePageSize
            }
        });
    }

    /// <summary>
    /// Получить детали задания
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<TaskDto>> GetTaskById(Guid id)
    {
        var task = await _context.ShiftTasks
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null)
            return NotFound();

        return Ok(task.ToDto());
    }

    /// <summary>
    /// Создать новое задание (ССЗ)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<TaskDto>> CreateTask([FromBody] CreateTaskRequestDto request)
    {
        var mold = await _context.Molds.FindAsync(request.MoldId);
        if (mold == null || !mold.IsActive)
            return BadRequest("Указана неактивная или несуществующая пресс-форма.");

        var task = new Core.Entities.ShiftTask
        {
            ImmId = request.ImmId,
            MoldId = request.MoldId,
            PersonnelId = request.PersonnelId,
            PlanQuantity = request.PlanQuantity,
            Note = request.Note,
            Status = Core.Enums.TaskStatus.Draft,
            PlannedDate = request.PlannedDate.HasValue
                ? DateTime.SpecifyKind(request.PlannedDate.Value, DateTimeKind.Utc)
                : null,
            IssuedAt = DateTime.UtcNow
        };

        _context.ShiftTasks.Add(task);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTaskById), new { id = task.Id }, task.ToDto());
    }

    /// <summary>
    /// Обновить задание
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdateTask(Guid id, [FromBody] UpdateTaskRequestDto request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (request.PlanQuantity.HasValue)
            task.PlanQuantity = request.PlanQuantity.Value;
        if (request.Note != null)
            task.Note = request.Note;
        if (request.PlannedDate.HasValue)
        {
            if (task.Status != Core.Enums.TaskStatus.Draft)
                return BadRequest("Плановую дату можно изменить только в черновике");
            var today = DateTime.UtcNow.Date;
            if (request.PlannedDate.Value.Date < today)
                return BadRequest("Плановая дата не может быть в прошлом");
            task.PlannedDate = DateTime.SpecifyKind(request.PlannedDate.Value, DateTimeKind.Utc);
        }
        if (request.Status.HasValue)
            task.Status = request.Status.Value;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Выдать задание (перевести из черновика в статус Issued)
    /// </summary>
    [HttpPost("{id:guid}/issue")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> IssueTask(Guid id)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.Issue();

        await _context.SaveChangesAsync();

        return Ok(new { message = "Задание выдано" });
    }

    /// <summary>
    /// Начать наладку — перевести задание в статус Setup
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Authorize(Roles = $"{Roles.Adjuster}")]
    public async Task<IActionResult> StartTask(Guid id)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.StartSetup();

        await _context.SaveChangesAsync();
        await _emulator.SetModeAsync(task.ImmId.ToString(), "manual");

        return Ok(new { message = "Наладка начата" });
    }

    /// <summary>
    /// Завершить наладку — перевести задание в статус InProgress
    /// </summary>
    [HttpPost("{id:guid}/complete-setup")]
    [Authorize(Roles = $"{Roles.Adjuster}")]
    public async Task<IActionResult> CompleteSetup(Guid id)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.CompleteSetup();

        await _context.SaveChangesAsync();
        await _emulator.SetModeAsync(task.ImmId.ToString(), "auto");

        return Ok(new { message = "Наладка завершена, задание в работе" });
    }

    /// <summary>
    /// Отменить наладку — вернуть задание в статус Issued
    /// </summary>
    [HttpPost("{id:guid}/cancel-setup")]
    [Authorize(Roles = $"{Roles.Adjuster}")]
    public async Task<IActionResult> CancelSetup(Guid id)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.CancelSetup();

        await _context.SaveChangesAsync();
        await _emulator.SetModeAsync(task.ImmId.ToString(), "idle");

        return Ok(new { message = "Наладка отменена" });
    }

    /// <summary>
    /// Зафиксировать верификацию пресс-формы по QR-коду
    /// </summary>
    [HttpPost("{id:guid}/verify-mold")]
    [Authorize(Roles = $"{Roles.Adjuster}")]
    public async Task<IActionResult> VerifyMold(Guid id)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.VerifyMold();

        await _context.SaveChangesAsync();

        return Ok(new { message = "Пресс-форма верифицирована" });
    }

    /// <summary>
    /// Завершить задание
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager}")]
    public async Task<IActionResult> CompleteTask(Guid id, [FromBody] CompleteTaskRequestDto request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.Complete(request.ActualQuantity, request.CompletionReason);

        await _context.SaveChangesAsync();
        await _emulator.SetModeAsync(task.ImmId.ToString(), "idle");

        return Ok(new { message = "Задание завершено" });
    }

    /// <summary>
    /// Закрыть задание (ручное закрытие в конце дня)
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = $"{Roles.Manager},{Roles.Admin}")]
    public async Task<IActionResult> CloseTask(Guid id, [FromBody] CloseTaskRequestDto? request = null)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.Close(request?.CloseReason);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Задание закрыто" });
    }
}