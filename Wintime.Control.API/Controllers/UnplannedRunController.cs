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

            bool overlaps = tStart.HasValue && tEnd.HasValue
                && UnplannedRunAdjacency.Overlaps(tStart.Value, tEnd.Value, run.StartTime, episodeEnd);

            DateTime? firstActivity = hasCycles ? cyc!.First : tStart;
            bool after = firstActivity.HasValue
                && UnplannedRunAdjacency.AdjacentAfter(firstActivity.Value, episodeEnd, avg);

            bool before = hasCycles
                && UnplannedRunAdjacency.AdjacentBefore(cyc!.Last, run.StartTime, avg);

            var taskDate = t.PlannedDate ?? t.IssuedAt ?? t.CreatedAt;
            bool backdated = !hasCycles && UnplannedRunAdjacency.SameDate(taskDate, run.StartTime);

            if (!(overlaps || after || before || backdated))
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
