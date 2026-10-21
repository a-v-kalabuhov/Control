using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Planning;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Infrastructure.Services;

public class PlanConflictService : IPlanConflictService
{
    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly IReleaseForecastService _releaseForecast;
    private readonly IPlanningDefaultsService _planningDefaults;

    public PlanConflictService(ControlDbContext context, IFactoryClock factoryClock,
                               IReleaseForecastService releaseForecast, IPlanningDefaultsService planningDefaults)
    {
        _context = context;
        _factoryClock = factoryClock;
        _releaseForecast = releaseForecast;
        _planningDefaults = planningDefaults;
    }

    public async Task<PlanConflictsDto> GetAllAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var tasks = await _context.ShiftTasks
            .AsNoTracking()
            .Include(t => t.Imm)
            .Include(t => t.Mold)
            .Include(t => t.Product)
            .Where(t => OpenTaskStatuses.All.Contains(t.Status))
            .ToListAsync(ct);

        var schedule = await _factoryClock.GetScheduleAsync(ct);
        var defaults = await _planningDefaults.GetAsync(ct);
        var forecasts = await _releaseForecast.CalculateAsync(
            tasks.Where(t => t.Status == TaskStatus.InProgress).Select(t => t.Id).ToList(), nowUtc, ct);

        var refs = new Dictionary<Guid, ConflictTaskRefDto>(tasks.Count);
        var inputs = new List<ConflictTaskInput>(tasks.Count);
        foreach (var t in tasks)
        {
            var plannedStart = schedule.PlannedStartUtc(t.PlannedDate, t.PlannedSetupStartTime);
            var plan = TaskPlanCalculator.Calculate(plannedStart, t.PlannedSetupMinutes, t.PlanQuantity,
                t.Mold.Cavities, t.PlannedFullCycleSeconds, t.ReleaseBufferFixedMinutes, t.ReleaseBufferPercent);
            forecasts.TryGetValue(t.Id, out var forecast);
            var segments = TaskOccupancy.Build(plannedStart, plan, new OccupancyFacts(t.Status, t.SetupStartedAt),
                                               forecast, nowUtc);
            inputs.Add(new ConflictTaskInput(t.Id, t.ImmId, t.MoldId, segments));
            refs[t.Id] = new ConflictTaskRefDto
            {
                Id = t.Id, Status = t.Status, ImmId = t.ImmId, ImmName = t.Imm?.Name, MoldId = t.MoldId,
                MoldName = t.Mold.Name, ProductArticle = t.Product?.FullArticle, PlannedStartAt = plannedStart
            };
        }

        var conflicts = PlanConflictDetector.Detect(inputs, TimeSpan.FromMinutes(defaults.ConflictThresholdMinutes));
        var items = conflicts.Select(c => new PlanConflictDto
        {
            Kind = c.Kind, Resource = c.Resource, Task = refs[c.TaskId], Other = refs[c.OtherTaskId],
            OverlapStart = c.OverlapStart, OverlapEnd = c.OverlapEnd, OverlapSeconds = c.OverlapSeconds
        }).ToList();

        return new PlanConflictsDto
        {
            Now = nowUtc,
            ThresholdMinutes = defaults.ConflictThresholdMinutes,
            PlanCount = items.Count(i => i.Kind == ConflictKind.Plan),
            DeviationCount = items.Count(i => i.Kind == ConflictKind.Deviation),
            TaskCount = items.SelectMany(i => new[] { i.Task.Id, i.Other.Id }).Distinct().Count(),
            Items = items
        };
    }
}
