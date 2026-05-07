using Microsoft.Extensions.Logging;
using System.Globalization;
using Task = System.Threading.Tasks.Task;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Handlers;

public class ValidateTelemetryDataHandler : IValidateTelemetryDataHandler
{
    private readonly IImmCache _immCache;
    private readonly ILogger<ValidateTelemetryDataHandler> _logger;

    public ValidateTelemetryDataHandler(IImmCache immCache, ILogger<ValidateTelemetryDataHandler> logger)
    {
        _immCache = immCache;
        _logger = logger;
    }

    public Task<(bool, MqttProcessingContext)> ValidateAsync(MqttProcessingContext context)
    {
        // Part 1: validate sensor types and allowed values
        var (typeCheckPassed, validSensors) = ValidateTypes(context);
        if (!typeCheckPassed)
            return Task.FromResult<(bool, MqttProcessingContext)>((false, context));

        // Part 2: COV filtering — unchanged sensors are replaced with cached values (Variant B)
        var outputSensors = ApplyCovFilter(context, validSensors);

        var newMessage = new MqttTelemetryMessage
        {
            Timestamp = context.Data!.Timestamp,
            DeviceId = context.Data.DeviceId,
            Sensors = outputSensors
        };

        return Task.FromResult((true, context with { Data = newMessage }));
    }

    // --- Part 1: Type validation ---

    private (bool Success, Dictionary<string, string> Sensors) ValidateTypes(MqttProcessingContext context)
    {
        var sensors = context.Data!.Sensors;
        var sensorsByName = context.Template!.Sensors.ToDictionary(s => s.ParameterName);
        var invalidSensors = new List<string>();
        var validSensors = new Dictionary<string, string>(sensors.Count);

        foreach (var (name, value) in sensors)
        {
            if (!sensorsByName.TryGetValue(name, out var sensorTemplate))
                continue; // датчик не описан в шаблоне — пропускаем молча

            if (!TryValidateSensorValue(value, sensorTemplate, out var error))
            {
                invalidSensors.Add(name);
                _logger.LogWarning(
                    "IMM {ImmId}: sensor '{Sensor}' failed validation — {Error}",
                    context.Device!.Id, name, error);
                continue;
            }

            validSensors[name] = value;
        }

        // Mandatory sensors must survive type validation — if removed, the message is unusable
        foreach (var sensor in sensorsByName.Values.Where(s => s.Required))
        {
            if (!validSensors.ContainsKey(sensor.ParameterName))
            {
                _logger.LogError(
                    "IMM {ImmId}: mandatory sensor '{Sensor}' is missing or has invalid value, topic: {Topic}",
                    context.Device!.Id, sensor.ParameterName, context.Topic);
                return (false, validSensors);
            }
        }

        if (invalidSensors.Count > 0)
        {
            // TODO: raise schema mismatch event to notify administrator when event infrastructure is ready
            _logger.LogWarning(
                "IMM {ImmId}: {Count} sensor(s) removed due to type/value mismatch: [{Sensors}]",
                context.Device!.Id, invalidSensors.Count, string.Join(", ", invalidSensors));
        }

        return (true, validSensors);
    }

    private static bool TryValidateSensorValue(string value, SensorTemplate sensor, out string error)
    {
        error = string.Empty;

        var typeValid = sensor.ParameterType switch
        {
            "string"       => true,
            "float"        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "int"          => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            "boolean"      => bool.TryParse(value, out _),
            "cycleCounter" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            _              => false
        };

        if (!typeValid)
        {
            error = $"cannot parse '{value}' as '{sensor.ParameterType}'";
            return false;
        }

        if (sensor.AllowedValues is { Count: > 0 } && !sensor.AllowedValues.Contains(value))
        {
            error = $"'{value}' not in allowed values [{string.Join(", ", sensor.AllowedValues)}]";
            return false;
        }

        return true;
    }

    // --- Part 2: COV filtering (Variant B) ---
    // Sensors within threshold are replaced with the cached value so TimescaleDB/PostgreSQL
    // receives repeated values and can compress them efficiently.

    private Dictionary<string, string> ApplyCovFilter(
        MqttProcessingContext context,
        Dictionary<string, string> sensors)
    {
        var immId = context.Device!.Id;
        var template = context.Template!;
        var messageAt = DateTimeOffset.FromUnixTimeSeconds(context.Data!.Timestamp).UtcDateTime;

        var entry = _immCache.GetEntry(immId);

        if (entry == null)
        {
            // First message from this IMM — populate cache, skip filtering
            _immCache.AddImm(immId, template.DeviceTimeoutSeconds);
            _immCache.UpdateEntry(immId, messageAt, template.DeviceTimeoutSeconds, sensors);
            return new Dictionary<string, string>(sensors);
        }

        if (entry.LastMessageAt > messageAt)
        {
            // Out-of-order message — pass through without filtering
            _logger.LogWarning(
                "IMM {ImmId}: out-of-order message (msg={MessageAt:O}, cache={CacheAt:O}), COV skipped",
                immId, messageAt, entry.LastMessageAt);
            return new Dictionary<string, string>(sensors);
        }

        if (!entry.IsOnline)
        {
            // First message after offline — treat all values as changed, update cache
            _immCache.UpdateEntry(immId, messageAt, template.DeviceTimeoutSeconds, sensors);
            return new Dictionary<string, string>(sensors);
        }

        // Normal path: compare each sensor against its cached value
        var sensorsByName = template.Sensors.ToDictionary(s => s.ParameterName);
        var outputSensors = new Dictionary<string, string>(sensors.Count);
        // Start from existing cache so sensors absent in this message are preserved
        var newCacheValues = new Dictionary<string, string>(entry.SensorValues);

        foreach (var (name, value) in sensors)
        {
            if (!sensorsByName.TryGetValue(name, out var sensorTemplate))
                continue; // датчик не описан в шаблоне — игнорируем

            if (sensorTemplate.Threshold == 0)
            {
                // Порог не задан — всегда пропускаем текущее значение
                outputSensors[name] = value;
                newCacheValues[name] = value;
                continue;
            }

            if (entry.SensorValues.TryGetValue(name, out var cachedValue) &&
                !HasChangedBeyondThreshold(value, cachedValue, sensorTemplate))
            {
                // Change within threshold — substitute cached value (Variant B)
                outputSensors[name] = cachedValue;
                // newCacheValues[name] already holds cachedValue from entry.SensorValues
            }
            else
            {
                // Significant change or first appearance — use new value, update cache
                outputSensors[name] = value;
                newCacheValues[name] = value;
            }
        }

        _immCache.UpdateEntry(immId, messageAt, template.DeviceTimeoutSeconds, newCacheValues);
        return outputSensors;
    }

    private static bool HasChangedBeyondThreshold(string current, string cached, SensorTemplate sensor)
    {
        if (sensor.ParameterType is "float")
        {
            if (double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var cur) &&
                double.TryParse(cached, NumberStyles.Float, CultureInfo.InvariantCulture, out var cac))
                return Math.Abs(cur - cac) > (double)sensor.Threshold;
        }
        else if (sensor.ParameterType is "int" or "cycleCounter")
        {
            if (int.TryParse(current, out var cur) && int.TryParse(cached, out var cac))
                return Math.Abs(cur - cac) > (double)sensor.Threshold;
        }

        // string, boolean, or parse failure — any change is significant
        return current != cached;
    }
}