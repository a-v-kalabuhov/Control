using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.SDK;
using Wintime.Control.SDK.Mqtt;

namespace Wintime.Module.Imm;

/// <summary>
/// Модуль управления цехом ТПА (термопластавтоматов).
/// Регистрирует все сервисы, обработчики и сущности EF Core, специфичные для ТПА.
/// </summary>
public class ImmModule : IAppModule
{
    public string Key => "Imm";
    public string DisplayName => "Управление цехом ТПА";
    public Version ModuleVersion => new(1, 0, 0);
    public Version MinPlatformVersion => new(2, 0, 0);
    public IEnumerable<string> RequiredRoles => ["Adjuster"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Делегируем существующим extension-методам Infrastructure.
        // На следующем этапе рефакторинга эти методы будут перемещены сюда.
        services.AddMessageHandlers();
        services.AddImmStatusTracking();
        services.AddImmStatusWorkers();

        // Регистрируем MQTT-пайплайн модуля как реализацию общего интерфейса
        services.AddSingleton<IModuleMessagePipeline, ImmMessagePipeline>();
    }

    public void ConfigureModel(ModelBuilder modelBuilder)
    {
        // Конфигурации EF Core сущностей ТПА.
        // На следующем этапе будут вынесены из ControlDbContext.OnModelCreating сюда.
    }
}
