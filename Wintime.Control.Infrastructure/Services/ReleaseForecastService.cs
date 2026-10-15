using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Infrastructure.Services;

public class ReleaseForecastService : IReleaseForecastService
{
    private readonly ControlDbContext _context;

    public ReleaseForecastService(ControlDbContext context) => _context = context;

    public async Task<IReadOnlyDictionary<Guid, ReleaseForecastResult>> CalculateAsync(
        IReadOnlyCollection<Guid> taskIds, DateTime nowUtc, CancellationToken ct = default)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, ReleaseForecastResult>();

        var ids = taskIds.ToList();
        var inputs = await _context.ShiftTasks
            .Where(t => ids.Contains(t.Id) && t.Status == TaskStatus.InProgress)
            .Select(t => new { t.Id, t.PlanQuantity, t.ActualQuantity, t.PlannedFullCycleSeconds, t.Mold.Cavities })
            .ToListAsync(ct);
        if (inputs.Count == 0)
            return new Dictionary<Guid, ReleaseForecastResult>();

        var inProgressIds = inputs.Select(i => i.Id).ToList();
        // Открытые циклы (EndTime = null) в среднее не входят; null-паузы (наладка, простой, порог)
        // AVG пропускает — ADR-0017.
        var stats = await _context.ImmCycles
            .Where(c => c.TaskId.HasValue && inProgressIds.Contains(c.TaskId.Value) && c.EndTime != null)
            .GroupBy(c => c.TaskId!.Value)
            .Select(g => new
            {
                TaskId = g.Key,
                Closed = g.Count(),
                AvgDuration = g.Average(c => (double?)c.DurationSeconds) ?? 0,
                AvgPauseMs = g.Average(c => (double?)c.PauseDurationMs)
            })
            .ToDictionaryAsync(s => s.TaskId, ct);

        return inputs.ToDictionary(i => i.Id, i =>
        {
            stats.TryGetValue(i.Id, out var s);
            return ReleaseForecast.Calculate(nowUtc, i.PlanQuantity, i.ActualQuantity, i.Cavities,
                s?.Closed ?? 0, s?.AvgDuration ?? 0, s?.AvgPauseMs, i.PlannedFullCycleSeconds);
        });
    }
}
