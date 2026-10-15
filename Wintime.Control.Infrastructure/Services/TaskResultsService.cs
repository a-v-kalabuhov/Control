using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Infrastructure.Services;

public class TaskResultsService : ITaskResultsService
{
    private readonly ControlDbContext _context;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;

    public TaskResultsService(ControlDbContext context, IEffectiveStatusHistoryService effectiveStatusHistory)
    {
        _context = context;
        _effectiveStatusHistory = effectiveStatusHistory;
    }

    public async Task<TaskResultsDto?> GetAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = await _context.ShiftTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task == null)
            return null;

        var now = DateTime.UtcNow;
        if (TaskActivityWindow.Release(task, now) is not Interval window)
            return new TaskResultsDto { ReleaseStarted = false };

        return new TaskResultsDto
        {
            ReleaseStarted = true,
            ReleaseFrom = window.Start,
            ReleaseTo = window.End,
            InProgress = task.CompletedAt == null && task.ClosedAt == null,
            Output = new TaskOutputResultsDto
            {
                PlanQuantity = task.PlanQuantity,
                ActualQuantity = task.ActualQuantity,
                DefectQuantity = task.DefectQuantity,
                GoodQuantity = task.ActualQuantity - task.DefectQuantity,
                OverrunQuantity = Math.Max(0, task.ActualQuantity - task.PlanQuantity),
                // У Closed CloseReason — причина закрытия, она уже на вкладке «Данные задания».
                DeviationReason = task.Status == TaskStatus.Completed ? task.CloseReason : null
            },
            Cycles = await GetCyclesAsync(task.Id, task.PlannedInjectionCycleSeconds, task.PlannedFullCycleSeconds, ct),
            Time = await GetTimeAsync(task.ImmId, window, ct)
        };
    }

    private async Task<TaskCycleResultsDto> GetCyclesAsync(Guid taskId, int? plannedInjection, int? plannedFull,
        CancellationToken ct)
    {
        var agg = await _context.ImmCycles
            .Where(c => c.TaskId == taskId && c.IsSuccessful && c.EndTime != null)
            .GroupBy(c => c.TaskId)
            .Select(g => new
            {
                Count = g.Count(),
                AvgDuration = g.Average(c => (double)c.DurationSeconds),
                // null-паузы (наладка, простой, порог) и неизмеренное литьё AVG пропускает — ADR-0017
                AvgPauseMs = g.Average(c => (double?)c.PauseDurationMs),
                AvgInjectionMs = g.Average(c => (double?)c.InjectionDurationMs)
            })
            .FirstOrDefaultAsync(ct);

        var count = agg?.Count ?? 0;
        var enough = count >= ReleaseForecast.MinClosedCycles;
        return new TaskCycleResultsDto
        {
            Count = count,
            AvgInjectionSeconds = enough && agg!.AvgInjectionMs is double inj ? inj / 1000 : null,
            AvgFullCycleSeconds = enough ? agg!.AvgDuration + (agg.AvgPauseMs ?? 0) / 1000 : null,
            PlannedInjectionSeconds = plannedInjection,
            PlannedFullCycleSeconds = plannedFull
        };
    }

    private async Task<TaskTimeResultsDto> GetTimeAsync(Guid immId, Interval window, CancellationToken ct)
    {
        var inputs = (await _effectiveStatusHistory.GatherAsync(
            new[] { immId }, window.Start, window.End, window.End, ct))[immId];
        var segments = EffectiveStatusTimeline.Build(
            inputs.Raw, inputs.Tasks, inputs.Downtimes, window.Start, window.End);

        int Sum(string status) => (int)Math.Round(segments
            .Where(s => s.EffectiveStatus == status)
            .Sum(s => (s.End - s.Start).TotalSeconds));

        // Количество — из тех же строк событий, что и разбивка времени (один запрос GatherAsync)
        var downtimeCount = inputs.Downtimes.Count;

        return new TaskTimeResultsDto
        {
            WindowSeconds = (int)Math.Round((window.End - window.Start).TotalSeconds),
            ProductionSeconds = Sum(EffectiveStatus.Production),
            DowntimeSeconds = Sum(EffectiveStatus.Downtime),
            OfflineSeconds = Sum(EffectiveStatus.Offline),
            DowntimeCount = downtimeCount
        };
    }
}
