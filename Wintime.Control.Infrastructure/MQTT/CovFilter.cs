using Microsoft.Extensions.Caching.Memory;
using Wintime.Control.Shared.Settings;

namespace Wintime.Control.Infrastructure.MQTT;

public interface ICovFilter
{
    bool ShouldSave(string immId, string parameterName, decimal? newValue, DateTime timestamp);
    bool ShouldSave(string immId, string parameterName, string? newValue, DateTime timestamp);
    void UpdateLastValue(string immId, string parameterName, decimal value);
    void UpdateLastValue(string immId, string parameterName, string value);
    DateTime? GetLastUpdate(string immId, string parameterName);
}

public class CovFilter : ICovFilter
{
    private readonly IMemoryCache _cache;
    private readonly Dictionary<string, SensorThreshold> _thresholds;

    public CovFilter(IMemoryCache cache, Dictionary<string, SensorThreshold> thresholds)
    {
        _cache = cache;
        _thresholds = thresholds;
    }

    public bool ShouldSave(string immId, string parameterName, decimal? newValue, DateTime timestamp)
    {
        if (!newValue.HasValue)
            return false;

        var cacheKey = $"{immId}:{parameterName}:last";
        var thresholdKey = parameterName;

        if (!_thresholds.TryGetValue(thresholdKey, out var threshold))
            return true; // Нет порога — сохраняем всё

        if (threshold.ParameterType != "numeric")
            return true;

        var lastValue = _cache.Get<decimal?>(cacheKey);

        if (!lastValue.HasValue)
        {
            // Первое значение — всегда сохраняем
            _cache.Set(cacheKey, newValue.Value, TimeSpan.FromMinutes(60));
            return true;
        }

        var diff = Math.Abs(newValue.Value - lastValue.Value);

        if (diff > threshold.Threshold)
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

        if (!_thresholds.TryGetValue(thresholdKey, out var threshold))
            return true;

        if (threshold.ParameterType == "discrete")
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