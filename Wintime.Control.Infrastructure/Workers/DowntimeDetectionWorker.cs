using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Фоновый сервис, который при активном задании InProgress автоматически создаёт
/// запись простоя (Event типа Downtime), если ТПА дольше порога находится не в Auto,
/// и закрывает её при возврате в Auto. По образцу <see cref="ImmOfflineWorker"/>.
/// </summary>
public class DowntimeDetectionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImmStatusCache _statusCache;
    private readonly DowntimeSettings _settings;
    private readonly ILogger<DowntimeDetectionWorker> _logger;

    public DowntimeDetectionWorker(
        IServiceScopeFactory scopeFactory,
        IImmStatusCache statusCache,
        IOptions<DowntimeSettings> settings,
        ILogger<DowntimeDetectionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _statusCache = statusCache;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DowntimeDetectionWorker iteration failed");
            }
        }
    }

    /// <summary>Один проход опроса. Выделен для тестируемости.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entries = _statusCache.GetAll();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        foreach (var entry in entries)
        {
            try
            {
                var task = await db.ShiftTasks
                    .Where(t => t.ImmId == entry.ImmId
                             && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress))
                    .FirstOrDefaultAsync(ct);

                var taskStatus = ActiveTaskStatusMap.From(task?.Status);

                var openAuto = await db.Events
                    .Where(e => e.ImmId == entry.ImmId
                             && e.EventType == EventType.Downtime
                             && e.EndTime == null
                             && e.IsAuto)
                    .FirstOrDefaultAsync(ct);

                var hasAnyOpenDowntime = await db.Events
                    .AnyAsync(e => e.ImmId == entry.ImmId
                                && e.EventType == EventType.Downtime
                                && e.EndTime == null, ct);

                var outcome = DowntimeDecision.Evaluate(
                    entry.Status, entry.SinceUtc, now,
                    taskStatus, task?.StartedAt,
                    hasOpenAutoDowntime: openAuto is not null,
                    hasOpenManualDowntime: hasAnyOpenDowntime && openAuto is null,
                    thresholdSeconds: _settings.IdleThresholdSeconds);

                if (outcome.Action == DowntimeAction.Open)
                {
                    db.Events.Add(new Event
                    {
                        ImmId = entry.ImmId,
                        EventType = EventType.Downtime,
                        TaskId = task?.Id,
                        StartTime = outcome.At,
                        EndTime = null,
                        ReasonId = null,
                        IsAuto = true
                    });
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("IMM {ImmId}: авто-простой открыт с {Start}", entry.ImmId, outcome.At);
                }
                else if (outcome.Action == DowntimeAction.Close && openAuto is not null)
                {
                    openAuto.EndTime = outcome.At;
                    await db.SaveChangesAsync(ct);
                    _logger.LogInformation("IMM {ImmId}: авто-простой закрыт в {End}", entry.ImmId, outcome.At);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DowntimeDetectionWorker: обработка ТПА {ImmId} не удалась", entry.ImmId);
            }
        }
    }
}
