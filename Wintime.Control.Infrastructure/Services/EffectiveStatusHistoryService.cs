using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public class EffectiveStatusHistoryService : IEffectiveStatusHistoryService
{
    private readonly ControlDbContext _context;

    public EffectiveStatusHistoryService(ControlDbContext context) => _context = context;

    public async Task<IReadOnlyDictionary<Guid, EffectiveStatusInputs>> GatherAsync(
        IReadOnlyCollection<Guid> immIds, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo,
        CancellationToken ct = default)
    {
        DateTime ClampEnd(DateTime? end) => (end ?? effectiveTo) > effectiveTo ? effectiveTo : (end ?? effectiveTo);

        var ids = immIds.Distinct().ToList();

        var rawRows = await _context.ImmStatusHistory
            .Where(h => ids.Contains(h.ImmId) && h.ChangedAt < toUtc && (h.EndedAt == null || h.EndedAt > fromUtc))
            .OrderBy(h => h.ChangedAt)
            .Select(h => new { h.ImmId, h.Status, h.ChangedAt, h.EndedAt })
            .ToListAsync(ct);

        var taskRows = await _context.ShiftTasks
            .Where(t => ids.Contains(t.ImmId) && t.SetupStartedAt != null && t.SetupStartedAt < toUtc
                        // Нижняя граница: задание, закончившееся до окна, пересечься с ним не может.
                        && ((t.CompletedAt == null && t.ClosedAt == null) || (t.CompletedAt ?? t.ClosedAt) > fromUtc))
            .Select(t => new { t.ImmId, t.SetupStartedAt, t.StartedAt, t.CompletedAt, t.ClosedAt })
            .ToListAsync(ct);

        var downtimeRows = await _context.Events
            .Where(e => ids.Contains(e.ImmId) && e.EventType == EventType.Downtime
                        && e.StartTime < toUtc && (e.EndTime == null || e.EndTime > fromUtc))
            .Select(e => new { e.ImmId, e.StartTime, e.EndTime })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, EffectiveStatusInputs>(ids.Count);
        foreach (var id in ids)
        {
            var raw = rawRows
                .Where(r => r.ImmId == id)
                .Select(r => new RawSegment(r.Status, r.ChangedAt, ClampEnd(r.EndedAt)))
                .ToList();

            var tasks = new List<TaskInterval>();
            foreach (var t in taskRows.Where(t => t.ImmId == id))
            {
                var setupStart = t.SetupStartedAt!.Value;
                var setupEnd   = t.StartedAt ?? t.CompletedAt ?? t.ClosedAt ?? toUtc;
                tasks.Add(new TaskInterval(ActiveTaskStatus.Setup, setupStart, ClampEnd(setupEnd)));
                if (t.StartedAt != null)
                {
                    var workEnd = t.CompletedAt ?? t.ClosedAt ?? toUtc;
                    tasks.Add(new TaskInterval(ActiveTaskStatus.InProgress, t.StartedAt.Value, ClampEnd(workEnd)));
                }
            }

            var downtimes = downtimeRows
                .Where(d => d.ImmId == id)
                .Select(d => new Interval(d.StartTime, ClampEnd(d.EndTime)))
                .ToList();

            result[id] = new EffectiveStatusInputs(raw, tasks, downtimes);
        }

        return result;
    }
}
