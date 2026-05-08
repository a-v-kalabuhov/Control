using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Фоновый сервис, периодически записывающий метку времени активности приложения
/// в таблицу <c>AppHeartbeat</c> базы данных.
/// </summary>
/// <remarks>
/// Каждые 5 секунд обновляет единственную строку с <c>Id = 1</c> (или создаёт её
/// при первом запуске). Запись выполняется в отдельном потоке через <c>Task.Run</c>,
/// чтобы не блокировать основной цикл воркера при кратковременных задержках БД.
/// Сбои логируются как предупреждения — потеря одного тика не является критичной.
/// </remarks>
public class AppHeartbeatWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppHeartbeatWorker> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="AppHeartbeatWorker"/>.
    /// </summary>
    /// <param name="scopeFactory">Фабрика DI-скоупов для получения <see cref="ControlDbContext"/>.</param>
    /// <param name="logger">Логгер сервиса.</param>
    public AppHeartbeatWorker(IServiceScopeFactory scopeFactory, ILogger<AppHeartbeatWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Выполняет основной цикл воркера: каждые 5 секунд обновляет поле
    /// <c>LastHeartbeatAt</c> записи <see cref="AppHeartbeat"/> в базе данных.
    /// </summary>
    /// <param name="stoppingToken">Токен остановки хоста.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

                    var record = await db.AppHeartbeat.FindAsync(1);
                    if (record == null)
                        db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = DateTime.UtcNow });
                    else
                        record.LastHeartbeatAt = DateTime.UtcNow;

                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AppHeartbeatWorker failed to write heartbeat");
                }
            }, stoppingToken);
        }
    }
}
