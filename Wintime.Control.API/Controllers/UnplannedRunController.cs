using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.UnplannedRun;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/unplanned-runs")]
[Authorize]
public class UnplannedRunController : ControllerBase
{
    private readonly ControlDbContext _context;
    public UnplannedRunController(ControlDbContext context) => _context = context;

    /// <summary>Журнал эпизодов «работы без задания» с деривированными агрегатами.</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Adjuster}")]
    public async Task<ActionResult<IEnumerable<UnplannedRunDto>>> GetList(
        [FromQuery] Guid? immId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool? assigned = null)
    {
        if (from.HasValue) from = DateTime.SpecifyKind(from.Value, DateTimeKind.Utc);
        if (to.HasValue)   to   = DateTime.SpecifyKind(to.Value,   DateTimeKind.Utc);

        var query = _context.UnplannedRuns
            .Include(r => r.Imm)
            .Include(r => r.AssignedTask).ThenInclude(t => t!.Mold)
            .Include(r => r.AssignedTask).ThenInclude(t => t!.Personnel)
            .AsQueryable();

        if (immId.HasValue) query = query.Where(r => r.ImmId == immId.Value);
        if (from.HasValue)  query = query.Where(r => r.StartTime >= from.Value);
        if (to.HasValue)    query = query.Where(r => r.StartTime <= to.Value);
        if (assigned.HasValue)
            query = assigned.Value ? query.Where(r => r.AssignedTaskId != null)
                                   : query.Where(r => r.AssignedTaskId == null);

        var runs = await query.OrderByDescending(r => r.StartTime).ToListAsync();

        var dtos = new List<UnplannedRunDto>(runs.Count);
        foreach (var r in runs)
        {
            var agg = await ComputeAggregatesAsync(r.ImmId, r.StartTime, r.ClosedAt, r.AssignedTaskId);
            dtos.Add(new UnplannedRunDto
            {
                Id = r.Id,
                ImmId = r.ImmId,
                ImmName = r.Imm.Name,
                StartTime = r.StartTime,
                EndTime = agg.EndTime,
                CycleCount = agg.CycleCount,
                AvgCycleDuration = agg.AvgCycleDuration,
                IsClosed = r.ClosedAt != null,
                AssignedTaskId = r.AssignedTaskId,
                AssignedTaskLabel = r.AssignedTask == null ? null
                    : $"{r.AssignedTask.Mold.Name} · план {r.AssignedTask.PlanQuantity}",
                PersonnelName = r.AssignedTask?.Personnel?.FullName
            });
        }
        return Ok(dtos);
    }

    /// <summary>Задания-кандидаты для привязки: только этот ТПА, без разрыва во времени (UC-5).</summary>
    [HttpGet("{id:guid}/candidates")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<IEnumerable<TaskCandidateDto>>> GetCandidates(Guid id)
    {
        var run = await _context.UnplannedRuns.FindAsync(id);
        if (run == null) return NotFound("Эпизод не найден");

        var agg = await ComputeAggregatesAsync(run.ImmId, run.StartTime, run.ClosedAt, run.AssignedTaskId);
        var episodeEnd = agg.EndTime ?? run.StartTime;
        var avg = agg.AvgCycleDuration > 0 ? agg.AvgCycleDuration : 1; // защита от деления/нулевого порога

        // Все задания этого ТПА + агрегаты их циклов (первый/последний цикл, число)
        var tasks = await _context.ShiftTasks
            .Where(t => t.ImmId == run.ImmId)
            .Include(t => t.Mold)
            .Include(t => t.Personnel)
            .ToListAsync();

        var cycleAgg = await _context.ImmCycles
            .Where(c => c.ImmId == run.ImmId && c.TaskId != null)
            .GroupBy(c => c.TaskId!.Value)
            .Select(g => new { TaskId = g.Key, First = g.Min(c => c.StartTime), Last = g.Max(c => c.EndTime), Count = g.Count() })
            .ToListAsync();

        // Для каждого eligible-задания считаем признаки смежности один раз — переиспользуем
        // и для фильтра, и для приоритета рекомендации (без пересчёта).
        var eligible = new List<(Core.Entities.ShiftTask Task, bool Overlaps, bool After, bool Before)>();
        foreach (var t in tasks)
        {
            var cyc = cycleAgg.FirstOrDefault(a => a.TaskId == t.Id);
            bool hasCycles = cyc != null && cyc.Count > 0;

            DateTime? tStart = t.SetupStartedAt ?? t.StartedAt;
            DateTime? tEnd = t.ClosedAt ?? t.CompletedAt;
            var taskDate = t.PlannedDate ?? t.IssuedAt ?? t.CreatedAt;

            bool overlaps = tStart.HasValue && tEnd.HasValue
                && UnplannedRunAdjacency.Overlaps(tStart.Value, tEnd.Value, run.StartTime, episodeEnd);
            DateTime? firstActivity = hasCycles ? cyc!.First : tStart;
            bool after = firstActivity.HasValue
                && UnplannedRunAdjacency.AdjacentAfter(firstActivity.Value, episodeEnd, avg);
            bool before = hasCycles
                && UnplannedRunAdjacency.AdjacentBefore(cyc!.Last, run.StartTime, avg);

            bool isEligible = IsTaskAdjacentToEpisode(
                tStart, tEnd, hasCycles, firstActivity, hasCycles ? cyc!.Last : null,
                taskDate, run.StartTime, episodeEnd, avg);

            if (!isEligible)
                continue;

            eligible.Add((t, overlaps, after, before));
        }

        // Рекомендованный по приоритету: пересечение → сразу после → сразу перед.
        // Ровно один кандидат получает Recommended = true (первое совпадение по приоритету).
        Guid? recommendedId =
            eligible.FirstOrDefault(e => e.Overlaps).Task?.Id
            ?? eligible.FirstOrDefault(e => e.After).Task?.Id
            ?? eligible.FirstOrDefault(e => e.Before).Task?.Id;

        var candidates = eligible.Select(e =>
        {
            var taskDate = e.Task.PlannedDate ?? e.Task.IssuedAt ?? e.Task.CreatedAt;
            return new TaskCandidateDto
            {
                TaskId = e.Task.Id,
                Label = $"{e.Task.Mold.Name} · план {e.Task.PlanQuantity} · {taskDate:dd.MM.yyyy}",
                PersonnelName = e.Task.Personnel?.FullName,
                Recommended = e.Task.Id == recommendedId
            };
        }).ToList();

        return Ok(candidates);
    }

    /// <summary>Ретро-привязка эпизода к заданию: бэкфилл сирот-циклов + пересчёт выпуска (UC-6/UC-7).</summary>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTaskRequestDto request)
    {
        var run = await _context.UnplannedRuns.FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return NotFound("Эпизод не найден");

        var task = await _context.ShiftTasks.Include(t => t.Mold).FirstOrDefaultAsync(t => t.Id == request.TaskId);
        if (task == null) return NotFound("Задание не найдено");

        if (task.ImmId != run.ImmId)
            return BadRequest("Задание принадлежит другому ТПА");
        if (task.Mold.ProductTypeId == null)
            return BadRequest("У пресс-формы задания не задан тип изделия");

        var agg = await ComputeAggregatesAsync(run.ImmId, run.StartTime, run.ClosedAt, run.AssignedTaskId);
        var episodeEnd = agg.EndTime ?? run.StartTime;
        if (!await IsAdjacentAsync(task, run.StartTime, episodeEnd, agg.AvgCycleDuration))
            return BadRequest("Задание не смежно эпизоду по времени");

        // UC-7: откат прежней привязки (список циклов возвращается — они уже обнулены
        // in-memory, но ещё не сохранены, поэтому запрос сирот их не увидит — сливаем вручную)
        var rolledBackCycles = new List<Core.Entities.ImmCycle>();
        if (run.AssignedTaskId.HasValue && run.AssignedTaskId.Value != task.Id)
            rolledBackCycles = await RollbackBindingAsync(run, run.AssignedTaskId.Value);

        // Бэкфилл сирот-циклов окна эпизода
        var windowQuery = _context.ImmCycles.Where(c => c.ImmId == run.ImmId && c.TaskId == null && c.EndTime >= run.StartTime);
        if (run.ClosedAt.HasValue)
            windowQuery = windowQuery.Where(c => c.EndTime <= run.ClosedAt.Value);
        var orphanCycles = await windowQuery.ToListAsync();
        var cycles = orphanCycles
            .UnionBy(rolledBackCycles, c => c.Id)
            .ToList();

        decimal addedQty = 0, addedWeight = 0;
        foreach (var c in cycles)
        {
            c.TaskId = task.Id;
            c.MoldId = task.MoldId;
            c.Cavities = task.Mold.Cavities; // снапшот текущей гнёздности (ADR-0001)
            if (c.IsSuccessful)
            {
                addedQty += c.Cavities;
                addedWeight += c.Cavities * task.Mold.PartWeightGrams + task.Mold.RunnerWeightGrams;
            }
        }
        task.ActualQuantity += (int)addedQty;
        task.ActualMaterialWeightGrams += addedWeight;

        run.AssignedTaskId = task.Id;
        run.AssignedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        run.AssignedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Задание назначено" });
    }

    private async Task<bool> IsAdjacentAsync(Core.Entities.ShiftTask task, DateTime episodeStart, DateTime episodeEnd, double avgCycleDuration)
    {
        var avg = avgCycleDuration > 0 ? avgCycleDuration : 1;
        var cyc = await _context.ImmCycles
            .Where(c => c.TaskId == task.Id)
            .GroupBy(c => c.TaskId)
            .Select(g => new { First = g.Min(c => c.StartTime), Last = g.Max(c => c.EndTime), Count = g.Count() })
            .FirstOrDefaultAsync();
        bool hasCycles = cyc != null && cyc.Count > 0;

        DateTime? tStart = task.SetupStartedAt ?? task.StartedAt;
        DateTime? tEnd = task.ClosedAt ?? task.CompletedAt;
        DateTime? firstActivity = hasCycles ? cyc!.First : tStart;
        var taskDate = task.PlannedDate ?? task.IssuedAt ?? task.CreatedAt;

        return IsTaskAdjacentToEpisode(
            tStart, tEnd, hasCycles, firstActivity, hasCycles ? cyc!.Last : null,
            taskDate, episodeStart, episodeEnd, avg);
    }

    /// <summary>
    /// Единственное место, где определяется правило смежности задания эпизоду (a/b/c/d).
    /// Используется и <see cref="GetCandidates"/> (список кандидатов), и <see cref="IsAdjacentAsync"/>
    /// (проверка при назначении) — чтобы список кандидатов и фактическая проверка никогда не расходились.
    /// </summary>
    private static bool IsTaskAdjacentToEpisode(
        DateTime? taskWorkStart, DateTime? taskWorkEnd,
        bool hasCycles, DateTime? firstActivity, DateTime? lastCycleEnd,
        DateTime taskDate,
        DateTime episodeStart, DateTime episodeEnd, double avgCycleSeconds)
    {
        bool overlaps = taskWorkStart.HasValue && taskWorkEnd.HasValue
            && UnplannedRunAdjacency.Overlaps(taskWorkStart.Value, taskWorkEnd.Value, episodeStart, episodeEnd);
        bool after = firstActivity.HasValue
            && UnplannedRunAdjacency.AdjacentAfter(firstActivity.Value, episodeEnd, avgCycleSeconds);
        bool before = hasCycles && lastCycleEnd.HasValue
            && UnplannedRunAdjacency.AdjacentBefore(lastCycleEnd.Value, episodeStart, avgCycleSeconds);
        bool backdated = !hasCycles && UnplannedRunAdjacency.SameDate(taskDate, episodeStart);

        return overlaps || after || before || backdated;
    }

    private async Task<List<Core.Entities.ImmCycle>> RollbackBindingAsync(Core.Entities.UnplannedRun run, Guid previousTaskId)
    {
        var prevTask = await _context.ShiftTasks.Include(t => t.Mold).FirstOrDefaultAsync(t => t.Id == previousTaskId);
        var windowQuery = _context.ImmCycles.Where(c => c.ImmId == run.ImmId && c.TaskId == previousTaskId && c.EndTime >= run.StartTime);
        if (run.ClosedAt.HasValue)
            windowQuery = windowQuery.Where(c => c.EndTime <= run.ClosedAt.Value);
        var cycles = await windowQuery.ToListAsync();

        decimal qty = 0, weight = 0;
        foreach (var c in cycles)
        {
            if (c.IsSuccessful && prevTask != null)
            {
                qty += c.Cavities;
                weight += c.Cavities * prevTask.Mold.PartWeightGrams + prevTask.Mold.RunnerWeightGrams;
            }
            c.TaskId = null;
            c.MoldId = null;
            c.Cavities = 0;
        }
        if (prevTask != null)
        {
            prevTask.ActualQuantity -= (int)qty;
            prevTask.ActualMaterialWeightGrams -= weight;
        }
        return cycles;
    }

    private record Aggregates(int CycleCount, DateTime? EndTime, double AvgCycleDuration);

    /// <summary>Деривация агрегатов эпизода из ImmCycles (derive-on-read).</summary>
    private async Task<Aggregates> ComputeAggregatesAsync(
        Guid immId, DateTime startTime, DateTime? closedAt, Guid? assignedTaskId)
    {
        var q = _context.ImmCycles.Where(c => c.ImmId == immId && c.EndTime >= startTime);
        if (closedAt.HasValue)
            q = q.Where(c => c.EndTime <= closedAt.Value);
        // сироты + (после назначения) циклы назначенного задания
        q = assignedTaskId.HasValue
            ? q.Where(c => c.TaskId == null || c.TaskId == assignedTaskId.Value)
            : q.Where(c => c.TaskId == null);

        var list = await q.Select(c => new { c.EndTime, c.DurationSeconds }).ToListAsync();
        if (list.Count == 0)
            return new Aggregates(0, null, 0);
        return new Aggregates(list.Count, list.Max(c => c.EndTime), list.Average(c => c.DurationSeconds));
    }
}
