using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Фоновый сервис, периодически проверяющий состояние ТПА и переводящий
/// оборудование в статус <c>Offline</c>, если оно перестало выходить на связь.
/// </summary>
/// <remarks>
/// Каждые 5 секунд опрашивает <see cref="IImmCache"/>. Если ТПА помечен как
/// недоступный (<c>IsOnline == false</c>), но в <see cref="IImmStatusCache"/>
/// ещё не зафиксирован статус <c>Offline</c>, сервис записывает переход через
/// <see cref="IImmStatusService.UpdateStatusAsync"/>.
/// </remarks>
public class ImmOfflineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IImmCache _immCache;
    private readonly IImmStatusCache _statusCache;
    private readonly ILogger<ImmOfflineWorker> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ImmOfflineWorker"/>.
    /// </summary>
    /// <param name="scopeFactory">Фабрика DI-скоупов для получения скоупированных сервисов.</param>
    /// <param name="immCache">Кеш состояния подключения ТПА (singleton).</param>
    /// <param name="statusCache">Кеш актуальных статусов ТПА (singleton).</param>
    /// <param name="logger">Логгер сервиса.</param>
    public ImmOfflineWorker(
        IServiceScopeFactory scopeFactory,
        IImmCache immCache,
        IImmStatusCache statusCache,
        ILogger<ImmOfflineWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _immCache = immCache;
        _statusCache = statusCache;
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
                var entries = _immCache.GetAll();
                foreach (var entry in entries)
                {
                    if (!entry.IsOnline && _statusCache.GetStatus(entry.ImmId) != "Offline")
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<IImmStatusService>();
                        await svc.UpdateStatusAsync(entry.ImmId, "Offline", DateTime.UtcNow);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ImmOfflineWorker iteration failed");
            }
        }
    }
}
