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
    private readonly IFactoryClock _factoryClock;

    public TasksController(ControlDbContext context, IEmulatorControlService emulator, IFactoryClock factoryClock)
    {
        _context = context;
        _emulator = emulator;
        _factoryClock = factoryClock;
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
            .Include(t => t.Order)
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

        var schedule = await _factoryClock.GetScheduleAsync();
        var dtos = tasks.Select(t => t.ToDto(schedule)).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Мои задания — для текущего наладчика (по JWT): в работе, очередь по плановому началу, архив.
    /// </summary>
    /// <param name="search">Фильтр по названию ТПА или пресс-формы (регистронезависимый).</param>
    /// <param name="archivePage">Номер страницы архива (с 1).</param>
    /// <param name="archivePageSize">Размер страницы архива.</param>
    [HttpGet("my")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager},{Roles.Admin}")]
    public async Task<ActionResult<MyTasksDto>> GetMyTasks(
        [FromQuery] string? search = null,
        [FromQuery] int archivePage = 1,
        [FromQuery] int archivePageSize = 20)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

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

        var schedule = await _factoryClock.GetScheduleAsync();

        var active = await baseQuery
            .Where(t => t.Status == Core.Enums.TaskStatus.Setup
                     || t.Status == Core.Enums.TaskStatus.InProgress)
            .OrderBy(t => t.SetupStartedAt)
            .ToListAsync();

        // Плановое начало вычисляется (PlannedTaskStart), поэтому сортируем в памяти:
        // выданных заданий у одного наладчика — единицы.
        var queue = (await baseQuery
                .Where(t => t.Status == Core.Enums.TaskStatus.Issued)
                .ToListAsync())
            .Select(t => t.ToDto(schedule))
            .OrderBy(d => d.PlannedStartAt)
            .ToList();

        var archiveQuery = baseQuery.Where(t => t.Status == Core.Enums.TaskStatus.Completed
                                             || t.Status == Core.Enums.TaskStatus.Closed);
        var archiveTotal = await archiveQuery.CountAsync();
        var archiveItems = await archiveQuery
            .OrderByDescending(t => t.CompletedAt ?? t.ClosedAt)
            .ThenByDescending(t => t.Id)
            .Skip((archivePage - 1) * archivePageSize)
            .Take(archivePageSize)
            .ToListAsync();

        return Ok(new MyTasksDto
        {
            Active = active.Select(t => t.ToDto(schedule)).ToList(),
            Queue = queue,
            Archive = new PagedTasksDto
            {
                Items = archiveItems.Select(t => t.ToDto(schedule)).ToList(),
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
            .Include(t => t.Order)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null)
            return NotFound();

        var schedule = await _factoryClock.GetScheduleAsync();
        return Ok(task.ToDto(schedule));
    }

    /// <summary>Наладчик существует, активен и имеет роль Adjuster (PZP-13).</summary>
    private Task<bool> IsActiveAdjusterAsync(string personnelId) =>
        _context.Users.AnyAsync(u => u.Id == personnelId && u.IsActive && u.Role == UserRole.Adjuster);

    /// <summary>
    /// Ищет заказ по Id и проверяет правило привязки (PZP-05: активен, тип изделия совпадает
    /// с типом ПФ задания). Общая последовательность для CreateTask и SetOrder (M4).
    /// Возвращает найденный заказ, либо готовый ActionResult ошибки (заказ не найден — 400;
    /// нарушение правила привязки — DomainException, отдаётся глобальным хендлером как 400).
    /// </summary>
    private async Task<(Order? Order, ActionResult? Error)> ResolveOrderForBindingAsync(
        Guid orderId, Guid? moldProductTypeId)
    {
        var order = await _context.Orders.FindAsync(orderId);
        if (order == null)
            return (null, BadRequest("Указан несуществующий заказ."));

        Wintime.Control.Core.Policies.OrderTaskBinding.EnsureCanBind(order, moldProductTypeId);
        return (order, null);
    }

    /// <summary>
    /// Создать новое задание (ССЗ)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<TaskDto>> CreateTask([FromBody] CreateTaskRequestDto request)
    {
        if (request.PlannedDate is null)
            return BadRequest("Укажите плановую дату");

        var mold = await _context.Molds.FindAsync(request.MoldId);
        if (mold == null || !mold.IsActive)
            return BadRequest("Указана неактивная или несуществующая пресс-форма.");
        if (mold.ProductTypeId == null)
            return BadRequest("У пресс-формы не задан тип изделия.");

        var task = new Core.Entities.ShiftTask
        {
            ImmId = request.ImmId,
            MoldId = request.MoldId,
            PersonnelId = string.IsNullOrWhiteSpace(request.PersonnelId) ? null : request.PersonnelId,
            PlanQuantity = request.PlanQuantity,
            Note = request.Note,
            Status = Core.Enums.TaskStatus.Draft
        };

        var schedule = await _factoryClock.GetScheduleAsync();
        task.SetPlannedStart(request.PlannedDate.Value, request.PlannedSetupStartTime,
                             schedule.ToLocal(_factoryClock.UtcNow));

        task.SetCycleNorms(
            request.WorkMode,
            request.PlannedFullCycleSeconds,
            request.PlannedInjectionCycleSeconds,
            requireFullCycle: true);

        if (request.OrderId.HasValue)
        {
            var (order, error) = await ResolveOrderForBindingAsync(request.OrderId.Value, mold.ProductTypeId);
            if (error != null)
                return error;
            task.OrderId = order!.Id;
        }

        if (!string.IsNullOrWhiteSpace(request.PersonnelId) && !await IsActiveAdjusterAsync(request.PersonnelId))
            return BadRequest("Укажите активного наладчика");

        _context.ShiftTasks.Add(task);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTaskById), new { id = task.Id }, task.ToDto(schedule));
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
            task.SetPlanQuantity(request.PlanQuantity.Value);
        if (request.Note != null)
            task.SetNote(request.Note);
        bool planTouched = request.PlannedDate.HasValue
                        || request.PlannedSetupStartTime.HasValue
                        || request.ClearPlannedSetupStartTime;
        if (planTouched)
        {
            var schedule = await _factoryClock.GetScheduleAsync();
            var date = request.PlannedDate ?? task.PlannedDate;
            var time = request.ClearPlannedSetupStartTime
                ? null
                : request.PlannedSetupStartTime ?? task.PlannedSetupStartTime;
            task.SetPlannedStart(date, time, schedule.ToLocal(_factoryClock.UtcNow));
        }
        bool cycleFieldsTouched = request.WorkMode.HasValue
                               || request.PlannedFullCycleSeconds.HasValue
                               || request.PlannedInjectionCycleSeconds.HasValue;
        if (cycleFieldsTouched)
        {
            // Не переданные поля сохраняют текущее значение задания.
            // requireFullCycle: false — legacy-задание без эталона можно сохранить,
            // не вынуждая менеджера выдумывать цифру задним числом.
            task.SetCycleNorms(
                request.WorkMode ?? task.WorkMode,
                request.PlannedFullCycleSeconds ?? task.PlannedFullCycleSeconds,
                request.PlannedInjectionCycleSeconds ?? task.PlannedInjectionCycleSeconds,
                requireFullCycle: false);
        }
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Назначить, переназначить или снять наладчика (PZP-13).</summary>
    [HttpPut("{id:guid}/personnel")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> SetPersonnel(Guid id, [FromBody] SetTaskPersonnelRequestDto request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(request.PersonnelId) && !await IsActiveAdjusterAsync(request.PersonnelId))
            return BadRequest("Укажите активного наладчика");

        task.AssignPersonnel(request.PersonnelId);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Выдать задание («Планируется» → «Выдано»)
    /// </summary>
    [HttpPost("{id:guid}/issue")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> IssueTask(
        Guid id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)]
        IssueTaskRequestDto? request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        // Статус проверяет сама сущность (Issue), поэтому для не-Draft сначала вернётся ошибка статуса.
        if (task.Status == Core.Enums.TaskStatus.Draft
            && !string.IsNullOrWhiteSpace(request?.PersonnelId)
            && !await IsActiveAdjusterAsync(request.PersonnelId))
            return BadRequest("Укажите активного наладчика");

        task.Issue(request?.PersonnelId);

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

        // На ТПА одновременно допустимо только одно активное задание (Setup/InProgress).
        // Сначала завершите текущее, иначе циклы пойдут не тому заданию.
        var hasActiveTask = await _context.ShiftTasks.AnyAsync(t =>
            t.Id != task.Id &&
            t.ImmId == task.ImmId &&
            (t.Status == Core.Enums.TaskStatus.Setup || t.Status == Core.Enums.TaskStatus.InProgress));
        if (hasActiveTask)
            return BadRequest("На этом ТПА уже есть активное задание. Сначала завершите текущее задание.");

        task.StartSetup();

        // PZP-04: закрыть открытый эпизод «работы без задания» этого ТПА —
        // старт задания = детерминированная граница эпизода.
        var openRun = await _context.UnplannedRuns
            .Where(r => r.ImmId == task.ImmId && r.ClosedAt == null)
            .OrderBy(r => r.StartTime)
            .FirstOrDefaultAsync();
        if (openRun != null)
            openRun.ClosedAt = DateTime.UtcNow;

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

        task.Complete(request.ActualQuantity, request.CompletionReason, request.DefectQuantity);

        await _context.SaveChangesAsync();
        await _emulator.SetModeAsync(task.ImmId.ToString(), "idle");

        return Ok(new { message = "Задание завершено" });
    }

    /// <summary>
    /// PZP-08: увеличить план задания в работе (компенсация брака / допвыпуск).
    /// </summary>
    [HttpPost("{id:guid}/add-quantity")]
    [Authorize(Roles = $"{Roles.Adjuster},{Roles.Manager}")]
    public async Task<IActionResult> AddQuantity(Guid id, [FromBody] AddQuantityRequestDto request)
    {
        var task = await _context.ShiftTasks.FindAsync(id);
        if (task == null)
            return NotFound();

        task.AddPlannedQuantity(request.Delta);

        await _context.SaveChangesAsync();

        return Ok(new { message = "План задания увеличен", planQuantity = task.PlanQuantity });
    }

    /// <summary>
    /// PZP-05: привязать/сменить заказ задания из формы задания (UC-6).
    /// Отвязка отсюда недоступна (UC-7) — только через карточку заказа (DELETE /orders/{id}/tasks/{taskId}).
    /// </summary>
    [HttpPost("{id:guid}/set-order")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> SetOrder(Guid id, [FromBody] SetOrderRequestDto request)
    {
        var task = await _context.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (task == null)
            return NotFound();

        var (_, error) = await ResolveOrderForBindingAsync(request.OrderId, task.Mold?.ProductTypeId);
        if (error != null)
            return error;

        task.OrderId = request.OrderId;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Заказ задания обновлён" });
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