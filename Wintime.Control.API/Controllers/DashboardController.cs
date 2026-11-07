using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Dashboard;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly ControlDbContext _context;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;
    private readonly IFactoryClock _factoryClock;
    private readonly ITaskResultsService _taskResults;

    public DashboardController(
        ControlDbContext context,
        IEffectiveStatusHistoryService effectiveStatusHistory,
        IFactoryClock factoryClock,
        ITaskResultsService taskResults)
    {
        _context = context;
        _effectiveStatusHistory = effectiveStatusHistory;
        _factoryClock = factoryClock;
        _taskResults = taskResults;
    }

    /// <summary>Карточка ТПА литейщика: только текущая/последняя смена, окно считает сервер.</summary>
    [HttpGet("imm-card/{immId:guid}")]
    [Authorize(Roles = Roles.Operator)]
    public async Task<ActionResult<ImmCardShiftDto>> GetImmCardShift(
        Guid immId,
        CancellationToken cancellationToken)
    {
        var immExists = await _context.Imms.AnyAsync(
            i => i.Id == immId && i.IsActive, cancellationToken);
        if (!immExists)
            return NotFound();

        var (window, schedule, nowUtc) = await CurrentShiftAsync(cancellationToken);
        var effectiveTo = window.End < nowUtc ? window.End : nowUtc;
        var inputs = (await _effectiveStatusHistory.GatherAsync(
            new[] { immId }, window.Start, window.End, effectiveTo, cancellationToken))[immId];
        var segments = EffectiveStatusTimeline.Build(
            inputs.Raw, inputs.Tasks, inputs.Downtimes, window.Start, effectiveTo);

        var activeTasks = await TasksWithDetails()
            .Where(t => t.ImmId == immId)
            .Where(TaskActivityWindow.OverlapsPeriod(window.Start, window.End))
            .ToListAsync(cancellationToken);

        // Дата завода ±1 день — только широкий кандидатский диапазон для часовых поясов.
        // Точное ограничение по PlannedStartAt выполняется ниже после ToDto.
        var plannedFrom = DateOnly.FromDateTime(schedule.ToLocal(window.Start)).AddDays(-1);
        var plannedTo = DateOnly.FromDateTime(schedule.ToLocal(window.End)).AddDays(1);
        var plannedCandidates = await TasksWithDetails()
            .Where(t => t.ImmId == immId)
            .Where(t => t.Status != Core.Enums.TaskStatus.Completed
                     && t.Status != Core.Enums.TaskStatus.Closed)
            .Where(t => t.PlannedDate >= plannedFrom && t.PlannedDate <= plannedTo)
            .ToListAsync(cancellationToken);

        var plannedTasks = plannedCandidates
            .Select(t => t.ToDto(schedule))
            .Where(t => PlannedOverlaps(t.PlannedStartAt, t.PlannedSetupMinutes, window))
            .ToList();

        return Ok(new ImmCardShiftDto
        {
            ShiftStartAt = window.Start,
            ShiftEndAt = window.End,
            StatusSegments = segments.Select(s => new EffectiveStatusSegmentDto
            {
                EffectiveStatus = s.EffectiveStatus,
                ChangedAt = s.Start,
                EndedAt = s.End
            }).ToList(),
            ActiveTasks = activeTasks.Select(t => t.ToDto(schedule)).ToList(),
            PlannedTasks = plannedTasks
        });
    }

    /// <summary>Итоги задания литейщика, только если оно отображается в его карточке смены.</summary>
    [HttpGet("task-results/{taskId:guid}")]
    [Authorize(Roles = Roles.Operator)]
    public async Task<ActionResult<TaskResultsDto>> GetOperatorTaskResults(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var task = await _context.ShiftTasks
            .Include(t => t.Imm)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task == null || !task.Imm.IsActive)
            return NotFound();

        var (window, schedule, nowUtc) = await CurrentShiftAsync(cancellationToken);
        var activity = TaskActivityWindow.Activity(task, nowUtc);
        var activeInShift = activity != null
            && activity.Start < window.End
            && activity.End > window.Start;
        var plannedInShift = task.Status != Core.Enums.TaskStatus.Completed
            && task.Status != Core.Enums.TaskStatus.Closed
            && PlannedOverlaps(
                schedule.PlannedStartUtc(task.PlannedDate, task.PlannedSetupStartTime),
                task.PlannedSetupMinutes,
                window);
        if (!activeInShift && !plannedInShift)
            return NotFound();

        var results = await _taskResults.GetAsync(taskId, cancellationToken);
        return results == null ? NotFound() : Ok(results);
    }

    private async Task<(Interval Window, FactorySchedule Schedule, DateTime NowUtc)> CurrentShiftAsync(
        CancellationToken cancellationToken)
    {
        var schedule = await _factoryClock.GetScheduleAsync(cancellationToken);
        var shifts = await _context.Shifts.AsNoTracking().ToListAsync(cancellationToken);
        var nowUtc = _factoryClock.UtcNow;
        return (CurrentShiftWindow.Resolve(nowUtc, shifts, schedule.TimeZone), schedule, nowUtc);
    }

    private IQueryable<ShiftTask> TasksWithDetails() => _context.ShiftTasks
        .Include(t => t.Imm)
        .Include(t => t.Mold)
        .Include(t => t.Product).ThenInclude(p => p!.ProductType)
        .Include(t => t.Personnel)
        .Include(t => t.RecipeIngredients).ThenInclude(i => i.Material)
        .Include(t => t.RecipeIngredients).ThenInclude(i => i.OriginalMaterial)
        .Include(t => t.Order);

    private static bool PlannedOverlaps(DateTime plannedStartUtc, int setupMinutes, Interval window)
    {
        var plannedEndUtc = plannedStartUtc.AddMinutes(setupMinutes);
        return plannedEndUtc == plannedStartUtc
            ? plannedStartUtc >= window.Start && plannedStartUtc < window.End
            : plannedEndUtc > window.Start && plannedStartUtc < window.End;
    }

    /// <summary>
    /// Средняя загрузка цеха за период (взвешенная по времени).
    /// Загрузка = суммарное время станков в статусе Auto или Manual
    ///            / (длительность периода × количество активных станков).
    /// </summary>
    [HttpGet("shift-utilization")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster},{Roles.Observer},{Roles.Operator}")]
    public async Task<ActionResult<ShiftUtilizationDto>> GetShiftUtilization(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc   = DateTime.SpecifyKind(to,   DateTimeKind.Utc);

        if (fromUtc >= toUtc)
            return BadRequest("from должен быть меньше to");

        var activeImmIds = await _context.Imms
            .Where(i => i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();

        if (activeImmIds.Count == 0)
            return Ok(new ShiftUtilizationDto { From = fromUtc, To = toUtc });

        var segments = await _context.ImmStatusHistory
            .Where(h => activeImmIds.Contains(h.ImmId)
                     && h.ChangedAt < toUtc
                     && (h.EndedAt == null || h.EndedAt > fromUtc))
            .Select(h => new
            {
                h.Status,
                ChangedAt = h.ChangedAt,
                EndedAt   = h.EndedAt
            })
            .ToListAsync();

        double productiveSeconds = 0;

        foreach (var seg in segments)
        {
            if (seg.Status != "Auto" && seg.Status != "Manual")
                continue;

            var start = seg.ChangedAt < fromUtc ? fromUtc : seg.ChangedAt;
            var end   = (seg.EndedAt == null || seg.EndedAt > toUtc) ? toUtc : seg.EndedAt.Value;

            if (end > start)
                productiveSeconds += (end - start).TotalSeconds;
        }

        var totalPossibleSeconds = (toUtc - fromUtc).TotalSeconds * activeImmIds.Count;
        var utilization = totalPossibleSeconds > 0
            ? Math.Round((decimal)(productiveSeconds / totalPossibleSeconds * 100), 1)
            : 0m;

        return Ok(new ShiftUtilizationDto
        {
            Utilization  = utilization,
            MachineCount = activeImmIds.Count,
            From         = fromUtc,
            To           = toUtc
        });
    }
}
