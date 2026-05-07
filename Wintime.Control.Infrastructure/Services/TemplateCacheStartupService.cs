using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

/// <summary>
/// Фоновый сервис, выполняющийся при запуске приложения и предзагружающий
/// кэш шаблонов телеметрии из базы данных.
/// </summary>
/// <remarks>
/// Загружает все активные шаблоны (<see cref="Core.Entities.Template.IsActive"/> = true)
/// и помещает их в <see cref="ITemplateCache"/>, чтобы обработчики MQTT-сообщений
/// могли обращаться к ним без обращений к БД на горячем пути.
/// Шаблоны с некорректным <c>JsonConfig</c> пропускаются с предупреждением в лог.
/// </remarks>
public sealed class TemplateCacheStartupService(
    IServiceScopeFactory scopeFactory,
    ITemplateCache cache,
    ILogger<TemplateCacheStartupService> logger) : IHostedService
{
    /// <summary>
    /// Загружает активные шаблоны из БД и заполняет кэш при старте хоста.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены запуска хоста.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var templates = await db.Templates
            .Where(t => t.IsActive)
            .ToListAsync(cancellationToken);

        var loaded = 0;
        var failed = 0;

        foreach (var template in templates)
        {
            try
            {
                cache.Upsert(template);
                loaded++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(
                    "Template {TemplateId} ({TemplateName}) has invalid JsonConfig and was not cached: {Error}",
                    template.Id, template.Name, ex.Message);
            }
        }

        if (failed == 0)
            logger.LogInformation("Template cache loaded: {Count} templates", loaded);
        else
        {
            // TODO : Необходимо выполнить дополнительную проверку. Если есть активные IMM, зависящие от сломанных шаблонов, то нужно предпринять дополнительные действия.
            // 1. Такие IMM не могут обслуживаться системой, т.к. любое сообщение от них будет приводить к исключению отсутствия шаблона в кеше.
            // 2. Администратору системы надо исправить сломанные шаблоны, значит нужно уведомить администратора через события.
            // 3. Нужно помечать такие IMM статусом ошибки конфигурирования.
            // 4. Нужно учесть статус ошибки конфигурирования в эмуляторе - эмуляция таких IMM не должна запускаться.
                logger.LogWarning(
                    "Template cache loaded: {Loaded} templates, {Failed} failed validation",
                    loaded, failed);
        }
    }

    /// <summary>
    /// Вызывается при остановке хоста. Кэш очищается реализацией <see cref="ITemplateCache"/>,
    /// поэтому дополнительных действий не требуется.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены остановки хоста.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
