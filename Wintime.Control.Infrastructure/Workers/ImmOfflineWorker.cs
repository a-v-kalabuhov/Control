using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Фоновый сервис, периодически проверяющий состояние ТПА и переводящий
/// оборудование в статус <c>Offline</c>, если оно перестало выходить на связь.
/// </summary>
/// <remarks>
/// Каждые 5 секунд проверяет все ТПА, известные кешу статусов или <see cref="IImmCache"/>,
/// со статусом не <c>Offline</c>. Молчание считается от последнего MQTT-сообщения, а если
/// после старта API сообщений не было — от старта воркера: статус, восстановленный из БД
/// <c>ImmStatusStartupService</c>, при молчащем коннекторе иначе застывал бы навсегда.
/// Переход записывается через <see cref="IImmStatusService.UpdateStatusAsync"/>.
/// </remarks>
public class ImmOfflineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImmCache _immCache;
    private readonly IImmStatusCache _statusCache;
    private readonly ITemplateCache _templateCache;
    private readonly ILogger<ImmOfflineWorker> _logger;
    private readonly DateTime _watchStartedAtUtc = DateTime.UtcNow;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ImmOfflineWorker"/>.
    /// </summary>
    /// <param name="scopeFactory">Фабрика DI-скоупов для получения скоупированных сервисов.</param>
    /// <param name="immCache">Кеш состояния подключения ТПА (singleton).</param>
    /// <param name="statusCache">Кеш актуальных статусов ТПА (singleton).</param>
    /// <param name="templateCache">Кеш шаблонов — таймаут для ТПА без сообщений после старта.</param>
    /// <param name="logger">Логгер сервиса.</param>
    public ImmOfflineWorker(
        IServiceScopeFactory scopeFactory,
        IImmCache immCache,
        IImmStatusCache statusCache,
        ITemplateCache templateCache,
        ILogger<ImmOfflineWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _immCache = immCache;
        _statusCache = statusCache;
        _templateCache = templateCache;
        _logger = logger;
    }

    /// <summary>
    /// Выполняет основной цикл воркера: каждые 5 секунд проверяет все ТПА
    /// и фиксирует переход в <c>Offline</c> для тех, что вышли из сети.
    /// </summary>
    /// <param name="stoppingToken">Токен остановки хоста.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ImmOfflineWorker iteration failed");
            }
        }
    }

    /// <summary>Один проход проверки на момент <paramref name="nowUtc"/>. Выделен для тестируемости.</summary>
    internal async Task RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        var immIds = _statusCache.GetAll().Select(s => s.ImmId)
            .Union(_immCache.GetAll().Select(e => e.ImmId));

        foreach (var immId in immIds)
        {
            ct.ThrowIfCancellationRequested();
            if (_statusCache.GetStatus(immId) == ImmStatus.Offline)
                continue;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                if (IsOnline(immId, nowUtc, scope))
                    continue;

                var svc = scope.ServiceProvider.GetRequiredService<IImmStatusService>();
                await svc.UpdateStatusAsync(immId, ImmStatus.Offline, nowUtc);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Сбой по одному ТПА не должен задерживать Offline для остальных.
                _logger.LogError(ex, "ImmOfflineWorker: обработка ТПА {ImmId} не удалась", immId);
            }
        }
    }

    private bool IsOnline(Guid immId, DateTime nowUtc, IServiceScope scope)
    {
        var entry = _immCache.GetEntry(immId);
        if (entry is not null && entry.LastMessageAt != DateTime.MinValue)
            return entry.IsOnlineAt(nowUtc);

        // Сообщений после старта не было — отсчёт от старта воркера, таймаут из шаблона.
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var timeoutSeconds = DeviceTimeout.ForImm(db, _templateCache, immId);
        return (nowUtc - _watchStartedAtUtc).TotalSeconds < timeoutSeconds;
    }
}
