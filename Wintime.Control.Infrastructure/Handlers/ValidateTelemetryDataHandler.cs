using Microsoft.Extensions.Logging;
using System.Globalization;
using Task = System.Threading.Tasks.Task;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Обработчик конвейера телеметрии, выполняющий валидацию типов датчиков
/// и COV-фильтрацию (Change of Value) входящего MQTT-сообщения.
/// </summary>
/// <remarks>
/// Работа разбита на два этапа:
/// <list type="number">
///   <item>
///     <b>Валидация типов</b> — каждое значение датчика проверяется на соответствие
///     типу и списку допустимых значений из шаблона (<see cref="SensorTemplate"/>).
///     Датчики с неверными значениями исключаются; если среди них оказывается
///     обязательный датчик (<c>Required = true</c>), всё сообщение отклоняется.
///   </item>
///   <item>
///     <b>COV-фильтрация (Вариант B)</b> — значения, изменившиеся в пределах
///     порога (<c>Threshold</c>), заменяются кешированным значением, чтобы
///     PostgreSQL/TimescaleDB получал повторяющиеся строки и мог их эффективно
///     сжимать. Значения вне порога (или при первом появлении) сохраняются как есть.
///   </item>
/// </list>
/// </remarks>
public class ValidateTelemetryDataHandler : IValidateTelemetryDataHandler
{
    private readonly IImmCache _immCache;
    private readonly ILogger<ValidateTelemetryDataHandler> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ValidateTelemetryDataHandler"/>.
    /// </summary>
    /// <param name="immCache">Кеш состояния ТПА, используемый для COV-фильтрации.</param>
    /// <param name="logger">Логгер обработчика.</param>
    public ValidateTelemetryDataHandler(IImmCache immCache, ILogger<ValidateTelemetryDataHandler> logger)
    {
        _immCache = immCache;
        _logger = logger;
    }

    /// <summary>
    /// Валидирует и фильтрует телеметрическое сообщение.
    /// </summary>
    /// <param name="context">Контекст обработки MQTT-сообщения с декодированными данными и шаблоном.</param>
    /// <returns>
    /// Кортеж: <c>true</c> и обновлённый контекст с отфильтрованными датчиками —
    /// при успехе; <c>false</c> и исходный контекст — если отсутствует обязательный датчик.
    /// </returns>
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
            Mode = context.Data.Mode,
            Sensors = outputSensors
        };

        return Task.FromResult((true, context with { Data = newMessage }));
    }

    // --- Part 1: Type validation ---

    /// <summary>
    /// Проверяет типы и допустимые значения датчиков из входящего сообщения.
    /// </summary>
    /// <param name="context">Контекст обработки с декодированными данными и шаблоном.</param>
    /// <returns>
    /// Кортеж: признак успеха и словарь датчиков, прошедших валидацию.
    /// Возвращает <c>false</c>, если хотя бы один обязательный датчик не прошёл проверку.
    /// </returns>
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

    /// <summary>
    /// Проверяет одно значение датчика на соответствие типу и списку допустимых значений.
    /// </summary>
    /// <param name="value">Строковое значение датчика из сообщения.</param>
    /// <param name="sensor">Шаблон датчика с описанием типа, порога и допустимых значений.</param>
    /// <param name="error">Описание ошибки при неудаче; пустая строка при успехе.</param>
    /// <returns><see langword="true"/>, если значение корректно; иначе <see langword="false"/>.</returns>
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

    /// <summary>
    /// Применяет COV-фильтрацию (Вариант B) к набору проверенных датчиков.
    /// </summary>
    /// <remarks>
    /// Значения, изменившиеся в пределах порога, заменяются кешированным значением.
    /// Особые случаи: первое сообщение от ТПА, сообщение с нарушенным порядком (out-of-order)
    /// и первое сообщение после периода офлайн — пропускаются без фильтрации.
    /// После обработки кеш ТПА обновляется актуальными значениями.
    /// </remarks>
    /// <param name="context">Контекст обработки с данными устройства и шаблоном.</param>
    /// <param name="sensors">Датчики, прошедшие валидацию типов.</param>
    /// <returns>Итоговый словарь значений датчиков для передачи следующему обработчику.</returns>
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

    /// <summary>
    /// Определяет, превышает ли разница между текущим и кешированным значением датчика заданный порог.
    /// </summary>
    /// <param name="current">Новое значение датчика из входящего сообщения.</param>
    /// <param name="cached">Предыдущее значение датчика из кеша.</param>
    /// <param name="sensor">Шаблон датчика с типом и порогом изменения.</param>
    /// <returns>
    /// <see langword="true"/>, если изменение превышает порог или значения не удалось распарсить;
    /// <see langword="false"/>, если изменение находится в пределах порога.
    /// Для типов <c>string</c> и <c>boolean</c> любое несовпадение считается значимым изменением.
    /// </returns>
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