using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wintime.Control.SDK;

/// <summary>
/// Контракт плагина-модуля. Каждый модуль реализует этот интерфейс и регистрирует
/// свои сервисы, сущности EF Core и MQTT-обработчики.
/// </summary>
public interface IAppModule
{
    string Key { get; }
    string DisplayName { get; }
    Version ModuleVersion { get; }

    /// <summary>
    /// Минимальная версия платформы, с которой совместим модуль.
    /// </summary>
    Version MinPlatformVersion { get; }

    /// <summary>
    /// Роли, которые должны быть засеяны в БД при первом включении модуля.
    /// </summary>
    IEnumerable<string> RequiredRoles { get; }

    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Регистрирует конфигурации EF Core сущностей модуля в общем DbContext.
    /// </summary>
    void ConfigureModel(ModelBuilder modelBuilder);
}
