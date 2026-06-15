using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Services;

/// <summary>
/// Стартовый сервис, восстанавливающий кеш статусов ТПА из базы данных
/// при запуске приложения.
/// </summary>
/// <remarks>
/// Выполняет четыре шага в <see cref="StartAsync"/>:
/// <list type="number">
///   <item>Читает метку последнего пульса приложения (<c>AppHeartbeat</c>).</item>
///   <item>
///     Закрывает незавершённые записи <c>ImmStatusHistory</c> со статусом, отличным
///     от <c>Offline</c>: если порог (<c>lastHeartbeat + 2 × DeviceTimeoutSeconds</c>)
///     уже прошёл — вставляет запись <c>Offline</c> и закрывает предыдущую.
///   </item>
///   <item>Загружает актуальные (открытые) статусы в <see cref="IImmStatusCache"/>.</item>
///   <item>Обновляет (или создаёт) запись пульса приложения.</item>
/// </list>
/// Должен регистрироваться <b>до</b> <c>ImmOfflineWorker</c>, чтобы кеш был заполнен
/// прежде, чем воркер начнёт проверять переходы в Offline.
/// </remarks>
public class ImmStatusStartupService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImmStatusCache _statusCache;
    private readonly ITemplateCache _templateCache;
    private readonly ILogger<ImmStatusStartupService> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ImmStatusStartupService"/>.
    /// </summary>
    /// <param name="scopeFactory">Фабрика DI-скоупов для получения <see cref="ControlDbContext"/>.</param>
    /// <param name="statusCache">Кеш текущих статусов ТПА, заполняемый при старте.</param>
    /// <param name="templateCache">Кеш шаблонов для чтения <c>DeviceTimeoutSeconds</c>.</param>
    /// <param name="logger">Логгер сервиса.</param>
    public ImmStatusStartupService(
        IServiceScopeFactory scopeFactory,
        IImmStatusCache statusCache,
        ITemplateCache templateCache,
        ILogger<ImmStatusStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _statusCache = statusCache;
        _templateCache = templateCache;
        _logger = logger;
    }

    /// <summary>
    /// Выполняет сверку истории статусов с последним пульсом приложения
    /// и наполняет кеш актуальными статусами ТПА.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запуска хоста.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        // 1. Read last known heartbeat
        var heartbeat = await db.AppHeartbeat.FindAsync([1], cancellationToken);
        DateTime? lastHeartbeat = heartbeat?.LastHeartbeatAt;

        _logger.LogInformation("ImmStatusStartupService: lastHeartbeat={LastHeartbeat}", lastHeartbeat?.ToString("O") ?? "null (first run)");

        // 2. Reconcile open non-Offline status records
        var openRecords = await db.ImmStatusHistory
            .Where(h => h.EndedAt == null && h.Status != ImmStatus.Offline)
            .ToListAsync(cancellationToken);

        if (openRecords.Count > 0)
        {
            foreach (var record in openRecords)
            {
                DateTime closeTime;
                bool shouldClose;

                if (lastHeartbeat == null)
                {
                    shouldClose = true;
                    closeTime = DateTime.UtcNow;
                }
                else
                {
                    var timeoutSeconds = GetDeviceTimeoutSeconds(db, record.ImmId);
                    var threshold = lastHeartbeat.Value.AddSeconds(2 * timeoutSeconds);
                    shouldClose = threshold <= DateTime.UtcNow;
                    closeTime = threshold;
                }

                if (shouldClose)
                {
                    record.EndedAt = closeTime;
                    db.ImmStatusHistory.Add(new ImmStatusHistory
                    {
                        ImmId = record.ImmId,
                        Status = ImmStatus.Offline,
                        ChangedAt = closeTime,
                        EndedAt = null
                    });
                    _logger.LogInformation("IMM {ImmId}: closed stale status '{Status}', inserted Offline at {CloseTime:O}", record.ImmId, record.Status, closeTime);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        // 3. Load current statuses into cache
        var currentStatuses = await db.ImmStatusHistory
            .Where(h => h.EndedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var s in currentStatuses)
            _statusCache.SetStatus(s.ImmId, s.Status, s.ChangedAt);

        _logger.LogInformation("ImmStatusStartupService: loaded {Count} current statuses into cache", currentStatuses.Count);

        // 4. Upsert heartbeat
        if (heartbeat == null)
            db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = DateTime.UtcNow });
        else
            heartbeat.LastHeartbeatAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Не выполняет никаких действий при остановке хоста.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки хоста.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Возвращает таймаут устройства в секундах для указанного ТПА.
    /// </summary>
    /// <remarks>
    /// На момент запуска <c>IImmCache</c> ещё пуст, поэтому таймаут берётся из
    /// <see cref="ITemplateCache"/>, который заполняется раньше этого сервиса.
    /// Если шаблон не найден, возвращается значение по умолчанию — 60 секунд.
    /// </remarks>
    /// <param name="db">Контекст базы данных для получения <c>TemplateId</c> ТПА.</param>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <returns>Таймаут устройства в секундах.</returns>
    private int GetDeviceTimeoutSeconds(ControlDbContext db, Guid immId)
    {
        // IImmCache is empty at startup — use ITemplateCache (pre-loaded by TemplateCacheStartupService)
        var templateId = db.Imms
            .Where(i => i.Id == immId)
            .Select(i => i.TemplateId)
            .FirstOrDefault();

        if (templateId == default)
            return 60;

        return _templateCache.GetById(templateId)?.DeviceTimeoutSeconds ?? 60;
    }
}
