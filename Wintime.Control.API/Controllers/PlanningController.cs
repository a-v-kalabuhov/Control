using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Planning;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlanningController : ControllerBase
{
    /// <summary>Максимальная длина окна доски, сутки (месяц с запасом).</summary>
    public const int MaxBoardDays = 62;

    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IReleaseForecastService _releaseForecast;

    public PlanningController(ControlDbContext context, IFactoryClock factoryClock,
                              IReleaseForecastService releaseForecast)
    {
        _context = context;
        _factoryClock = factoryClock;
        _releaseForecast = releaseForecast;
    }

    /// <summary>
    /// План-график цеха (PZP-07a): активные ТПА (по названию) и архивные с заданиями в окне; задания,
    /// у которых плановое окно или окно активности пересекает <c>[from, to)</c>. План, факт,
    /// прогноз и остаток буфера — <see cref="PlanningBoardCalculator"/>, не хранятся.
    /// </summary>
    [HttpGet("board")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Observer}")]
    public async Task<ActionResult<PlanningBoardDto>> GetBoard([FromQuery] DateTime from, [FromQuery] DateTime to,
                                                               CancellationToken ct)
    {
        var fromUtc = ToUtc(from);
        var toUtc = ToUtc(to);
        if (toUtc <= fromUtc)
            return BadRequest("Конец периода должен быть позже начала");
        if ((toUtc - fromUtc).TotalDays > MaxBoardDays)
            return BadRequest($"Период не может быть длиннее {MaxBoardDays} суток");

        var now = DateTime.UtcNow;
        var schedule = await _factoryClock.GetScheduleAsync(ct);

        // План не хранится: в EF грубый отбор по плановой дате завода с запасом,
        // точный — по рассчитанному плановому окну ниже.
        var dateFrom = DateOnly.FromDateTime(schedule.ToLocal(fromUtc)).AddDays(-2);
        var dateTo = DateOnly.FromDateTime(schedule.ToLocal(toUtc)).AddDays(1);

        var byPlan = await TasksQuery()
            .Where(t => t.PlannedDate >= dateFrom && t.PlannedDate <= dateTo)
            .ToListAsync(ct);
        var byActivity = await TasksQuery()
            .Where(TaskActivityWindow.OverlapsPeriod(fromUtc, toUtc))
            .ToListAsync(ct);
        var activeIds = byActivity.Select(t => t.Id).ToHashSet();
        var candidates = byPlan.Concat(byActivity).DistinctBy(t => t.Id).ToList();

        var forecasts = await _releaseForecast.CalculateAsync(
            candidates.Where(t => t.Status == TaskStatus.InProgress).Select(t => t.Id).ToList(), now, ct);

        var items = new List<BoardTaskDto>();
        foreach (var t in candidates)
        {
            var dto = t.ToDto(schedule);
            // План уже посчитан в ToDto (TaskPlanCalculator) — собираем его обратно, без повторного расчёта.
            var plan = new TaskPlan(t.PlannedSetupMinutes * 60L, dto.PlannedReleaseSeconds,
                                    dto.PlannedReleaseBufferSeconds, dto.PlannedEndAt);
            forecasts.TryGetValue(t.Id, out var forecast);
            var timing = PlanningBoardCalculator.Build(dto.PlannedStartAt, plan,
                new BoardTaskFacts(t.Status, t.SetupStartedAt, t.StartedAt, t.CompletedAt, t.ClosedAt),
                forecast, now);

            if (!activeIds.Contains(t.Id) && !PlanningBoardCalculator.PlanOverlaps(timing.Plan, fromUtc, toUtc))
                continue;

            items.Add(new BoardTaskDto
            {
                Task = dto,
                ProductArticle = t.Product?.FullArticle,
                Plan = timing.Plan,
                Fact = timing.Fact,
                BufferRemainingSeconds = timing.BufferRemainingSeconds,
                OverdueNotStartedSeconds = timing.OverdueNotStartedSeconds,
                SetupLagSeconds = timing.SetupLagSeconds
            });
        }

        var immIdsWithTasks = items.Select(i => i.Task.ImmId).ToHashSet();
        var imms = await _context.Imms
            .Where(i => i.IsActive || immIdsWithTasks.Contains(i.Id))
            .OrderBy(i => i.Name)
            .Select(i => new BoardImmDto { Id = i.Id, Name = i.Name, IsActive = i.IsActive })
            .ToListAsync(ct);

        var byImm = items.ToLookup(i => i.Task.ImmId);
        return Ok(new PlanningBoardDto
        {
            From = fromUtc,
            To = toUtc,
            Now = now,
            Rows = imms.Select(imm => new PlanningBoardRowDto
            {
                Imm = imm,
                Tasks = byImm[imm.Id].OrderBy(i => i.Plan.SetupStart).ThenBy(i => i.Task.Id).ToList()
            }).ToList()
        });
    }

    private IQueryable<ShiftTask> TasksQuery() => _context.ShiftTasks
        .AsNoTracking()
        .Include(t => t.Imm)
        .Include(t => t.Mold).ThenInclude(m => m.ProductType)
        .Include(t => t.Product)
        .Include(t => t.Personnel)
        .Include(t => t.Order);

    // Биндер DateTime (AdjustToUniversal): ISO с «Z»/смещением даёт Kind=Utc, без суффикса — Unspecified (= UTC).
    // Ветка Local — страховка.
    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
