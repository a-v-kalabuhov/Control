using Microsoft.Extensions.Caching.Memory;
using Wintime.Control.Core.Entities;
using Wintime.Control.Shared.Settings;

namespace Wintime.Control.Infrastructure.MQTT;

/// <summary>
/// Change of Value Filter.
/// Этот класс по идее должен выполнять проверку поступивших значений показаний датчиков.
/// Deadband-фильтрация.
/// Суть проверки в том, что если у датчика изменение показания от предыдущего не превысило порог,
/// то сохранять его в БД нет смысла.
/// Хотя возможно сохранять смысл есть, а вто запускать каие-то вычисления смысла нет.
/// </summary>
public interface ICovFilter
{
    bool ShouldSave(string immId, string parameterName, decimal? newValue, DateTime timestamp);
    bool ShouldSave(string immId, string parameterName, string? newValue, DateTime timestamp);
    void UpdateLastValue(string immId, string parameterName, decimal value);
    void UpdateLastValue(string immId, string parameterName, string value);
    DateTime? GetLastUpdate(string immId, string parameterName);
}

// TODO : для корректной работы этого класса нужно написать сервис с кешем показаний.
// NOTE : сервис кеша показаний должен учитывать не только сами показания датчика, но и статус оборудования - оффлайн и т.п.
// NOTE : у оборудования сейчас вообще никак не обозначается его статус offline
// NOTE : по идее можно этот статус иметь не у всего оборудования, а у конкретного датчика (надо это обдумать)
// TODO : Итог : - надо сделать сервис кеша и также в нём учитывать состояние датчков - оффлайн он или нет
// TODO : Состояние датчика надо как-то проверять, т.е. нужна настройка, что если долго нет связи, то считаем датчик оффлайн
// значит нужен ещё один воркер, который будет периодиечки проверять состояние датчиков оборудования, ставить его в оффлайн при необходимости и записывать это состояние в БД
// воркер должен также при каждом проходе вычислять следующее время запуска и засыпать до этого момента
public class CovFilter : ICovFilter
{
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, SensorTemplate> _templates;

    public CovFilter(IMemoryCache cache, Dictionary<string, SensorTemplate> templates)
    {
        _cache = cache;
        _templates = templates;
    }

    public bool ShouldSave(string immId, string parameterName, decimal? newValue, DateTime timestamp)
    {
        if (!newValue.HasValue)
            return false;

        var cacheKey = $"{immId}:{parameterName}:last";
        var thresholdKey = parameterName;

        if (!_templates.TryGetValue(thresholdKey, out var template))
            return true; // Нет порога — сохраняем всё

        if (template.ParameterType != "numeric")
            return true;

        var lastValue = _cache.Get<decimal?>(cacheKey);

        if (!lastValue.HasValue)
        {
            // Первое значение — всегда сохраняем
            _cache.Set(cacheKey, newValue.Value, TimeSpan.FromMinutes(60));
            return true;
        }

        var diff = Math.Abs(newValue.Value - lastValue.Value);

        if (diff > template.Threshold)
        {
            _cache.Set(cacheKey, newValue.Value, TimeSpan.FromMinutes(60));
            _cache.Set($"{immId}:{parameterName}:lastUpdate", timestamp, TimeSpan.FromMinutes(60));
            return true;
        }

        return false;
    }

    public bool ShouldSave(string immId, string parameterName, string? newValue, DateTime timestamp)
    {
        if (newValue == null)
            return false;

        var cacheKey = $"{immId}:{parameterName}:last";
        var thresholdKey = parameterName;

        if (!_templates.TryGetValue(thresholdKey, out var template))
            return true;

        if (template.ParameterType == "discrete")
        {
            var lastValue = _cache.Get<string>(cacheKey);
            if (lastValue != newValue)
            {
                _cache.Set(cacheKey, newValue, TimeSpan.FromMinutes(60));
                _cache.Set($"{immId}:{parameterName}:lastUpdate", timestamp, TimeSpan.FromMinutes(60));
                return true;
            }
            return false;
        }

        // Для строковых — любое изменение значимо
        var lastStrValue = _cache.Get<string>(cacheKey);
        if (lastStrValue != newValue)
        {
            _cache.Set(cacheKey, newValue, TimeSpan.FromMinutes(60));
            _cache.Set($"{immId}:{parameterName}:lastUpdate", timestamp, TimeSpan.FromMinutes(60));
            return true;
        }

        return false;
    }

    public void UpdateLastValue(string immId, string parameterName, decimal value)
    {
        _cache.Set($"{immId}:{parameterName}:last", value, TimeSpan.FromMinutes(60));
    }

    public void UpdateLastValue(string immId, string parameterName, string value)
    {
        _cache.Set($"{immId}:{parameterName}:last", value, TimeSpan.FromMinutes(60));
    }

    public DateTime? GetLastUpdate(string immId, string parameterName)
    {
        return _cache.Get<DateTime?>($"{immId}:{parameterName}:lastUpdate");
    }
}