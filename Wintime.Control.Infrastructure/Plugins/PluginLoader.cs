using System.Reflection;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Entities;
using Wintime.Control.SDK;

namespace Wintime.Control.Infrastructure.Plugins;

/// <summary>
/// Загружает модули-плагины из папки plugins/ при старте приложения.
/// Активирует только те модули, которые зарегистрированы в AppModules с IsEnabled=true.
/// Присутствие DLL в папке без записи в AppModules (например, после отката БД) — игнорируется.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Обнаруживает и загружает зарегистрированные модули.
    /// </summary>
    /// <param name="pluginsDirectory">Путь к папке с поддиректориями модулей.</param>
    /// <param name="registeredModules">
    /// Список модулей из AppModules таблицы БД с IsEnabled=true.
    /// Только они будут активированы.
    /// </param>
    /// <param name="platformVersion">Текущая версия платформы для проверки совместимости.</param>
    /// <param name="logger">Логгер.</param>
    public static IReadOnlyList<IAppModule> DiscoverModules(
        string pluginsDirectory,
        IReadOnlyList<AppModuleRecord> registeredModules,
        Version platformVersion,
        ILogger logger)
    {
        var result = new List<IAppModule>();

        if (!Directory.Exists(pluginsDirectory))
        {
            logger.LogWarning("Plugins directory not found: {Dir}", pluginsDirectory);
            return result;
        }

        foreach (var registered in registeredModules.Where(m => m.IsEnabled))
        {
            var moduleDir = Path.Combine(pluginsDirectory, registered.Key);
            if (!Directory.Exists(moduleDir))
            {
                logger.LogError(
                    "Module {Key} is registered in DB but its directory '{Dir}' not found. Skipping.",
                    registered.Key, moduleDir);
                continue;
            }

            var dllFiles = Directory.GetFiles(moduleDir, "*.dll");
            IAppModule? loaded = null;

            foreach (var dll in dllFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var moduleType = assembly.GetExportedTypes()
                        .FirstOrDefault(t => typeof(IAppModule).IsAssignableFrom(t)
                                             && !t.IsAbstract && !t.IsInterface);
                    if (moduleType is null)
                        continue;

                    var instance = (IAppModule)Activator.CreateInstance(moduleType)!;

                    if (instance.Key != registered.Key)
                    {
                        logger.LogWarning(
                            "Module key mismatch: expected '{Expected}', got '{Actual}'. Skipping.",
                            registered.Key, instance.Key);
                        continue;
                    }

                    if (instance.MinPlatformVersion > platformVersion)
                    {
                        logger.LogError(
                            "Module {Key} requires platform >= {Required}, current is {Current}. Skipping.",
                            registered.Key, instance.MinPlatformVersion, platformVersion);
                        break;
                    }

                    loaded = instance;
                    logger.LogInformation(
                        "Module loaded: {Key} v{Version}", instance.Key, instance.ModuleVersion);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load assembly {Dll}", dll);
                }
            }

            if (loaded is not null)
                result.Add(loaded);
            else
                logger.LogError("No valid IAppModule implementation found in {Dir}", moduleDir);
        }

        return result;
    }
}
