using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Behaviors;
using Wintime.Control.Infrastructure.Cache;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Infrastructure.Services;
using Wintime.Control.Infrastructure.Workers;

/// <summary>
/// Методы расширения <see cref="IServiceCollection"/> для регистрации
/// инфраструктурных сервисов обработки MQTT-телеметрии и отслеживания состояния ТПА.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует конвейер обработки MQTT-сообщений на основе bounded channel.
    /// </summary>
    /// <remarks>
    /// Создаёт единственный <see cref="Channel{T}"/> ёмкостью 25 000 сообщений
    /// с режимом <see cref="BoundedChannelFullMode.Wait"/> и запускает
    /// <c>ProcessorCount × 2</c> экземпляров <c>MqttTelemetryWorker</c> в качестве
    /// hosted-сервисов, конкурентно читающих из канала.
    /// </remarks>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
    public static IServiceCollection AddMessageProcessing(this IServiceCollection services)
    {
        var capacity = 25000;
        var channel = Channel.CreateBounded<MqttProcessingContext>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
        services.AddSingleton(channel);
        var workerCount = Environment.ProcessorCount * 2;
        for (int i = 0; i < workerCount; i++)
            services.AddHostedService(sp => new MqttTelemetryWorker(channel.Reader, sp));

        return services;
    }

    /// <summary>
    /// Регистрирует обработчики шагов конвейера телеметрии как scoped-сервисы.
    /// </summary>
    /// <remarks>
    /// Регистрируются обработчики в порядке их вызова в pipeline:
    /// декодирование → валидация → сохранение → обновление статуса ТПА → обработка циклов.
    /// Сам <see cref="MessageProcessingPipeline"/> регистрируется как scoped и получает
    /// обработчики через конструктор.
    /// </remarks>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
    public static IServiceCollection AddMessageHandlers(this IServiceCollection services)
    {
        services.AddScoped<IDecodeTelemetryDataHandler, DecodeTelemetryDataHandler>();
        services.AddScoped<IValidateTelemetryDataHandler, ValidateTelemetryDataHandler>();
        services.AddScoped<IStoreTelemetryDataHandler, StoreTelemetryDataHandler>();
        services.AddScoped<IUpdateImmStatusHandler, UpdateImmStatusHandler>();
        services.AddScoped<ICycleProcessingHandler, CycleProcessingHandler>();
        services.AddSingleton<ICycleTracker, CycleTracker>();
        services.AddScoped<MessageProcessingPipeline>();
        return services;
    }

    /// <summary>
    /// Регистрирует сервисы отслеживания статусов ТПА.
    /// </summary>
    /// <remarks>
    /// <see cref="IImmStatusCache"/> регистрируется как singleton (in-memory кеш),
    /// <see cref="IImmStatusService"/> — как scoped (работает с БД через EF Core).
    /// </remarks>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
    public static IServiceCollection AddImmStatusTracking(this IServiceCollection services)
    {
        services.AddSingleton<IImmStatusCache, MemoryImmStatusCache>();
        services.AddScoped<IImmStatusService, ImmStatusService>();
        return services;
    }

    /// <summary>
    /// Регистрирует фоновые воркеры жизненного цикла ТПА и пульса приложения.
    /// </summary>
    /// <remarks>
    /// Порядок регистрации фиксирован: <c>ImmStatusStartupService</c> должен
    /// восстановить состояние кеша из БД до того, как <c>ImmOfflineWorker</c>
    /// начнёт проверять статусы. <c>AppHeartbeatWorker</c> регистрируется последним
    /// и не зависит от двух предыдущих.
    /// </remarks>
    /// <param name="services">Коллекция сервисов приложения.</param>
    /// <returns>Та же коллекция сервисов для цепочки вызовов.</returns>
    public static IServiceCollection AddImmStatusWorkers(this IServiceCollection services)
    {
        // Порядок регистрации важен: стартап-сервис должен запуститься раньше воркеров
        services.AddHostedService<ImmStatusStartupService>();
        services.AddHostedService<ImmOfflineWorker>();
        services.AddHostedService<DowntimeDetectionWorker>();
        services.AddHostedService<AppHeartbeatWorker>();
        return services;
    }
}
