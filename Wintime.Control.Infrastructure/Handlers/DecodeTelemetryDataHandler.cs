using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Globalization;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Первый обработчик конвейера телеметрии: разбирает входящее MQTT-сообщение,
/// проверяет его структуру и обогащает контекст данными устройства и шаблона.
/// </summary>
/// <remarks>
/// Выполняет следующие шаги по порядку:
/// <list type="number">
///   <item>Проверяет формат топика (<c>control/imm/{guid}/telemetry</c>) и извлекает <c>deviceId</c>.</item>
///   <item>Парсит полезную нагрузку как JSON и проверяет наличие полей <c>timestamp</c> и <c>sensors</c>.</item>
///   <item>Убеждается, что объект <c>sensors</c> не пуст, и строит из него словарь строковых значений.</item>
///   <item>Проверяет существование устройства в БД и загружает его шаблон из кеша.</item>
///   <item>
///     Нормализует <c>timestamp</c>: принимает как Unix-число (секунды), так и ISO-строку,
///     и возвращает обогащённый <see cref="MqttProcessingContext"/> для следующего обработчика.
///   </item>
/// </list>
/// При любой ошибке возвращает <c>false</c> и исходный контекст без изменений.
/// </remarks>
public class DecodeTelemetryDataHandler : IDecodeTelemetryDataHandler
{
    private readonly ControlDbContext _dbContext;
    private readonly ITemplateCache _templateCache;
    private readonly ILogger<DecodeTelemetryDataHandler> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DecodeTelemetryDataHandler"/>.
    /// </summary>
    /// <param name="dbContext">Контекст EF Core для поиска устройства по <c>deviceId</c>.</param>
    /// <param name="templateCache">Кеш шаблонов для получения конфигурации датчиков.</param>
    /// <param name="logger">Логгер обработчика.</param>
    public DecodeTelemetryDataHandler(
        ControlDbContext dbContext,
        ITemplateCache templateCache,
        ILogger<DecodeTelemetryDataHandler> logger)
    {
        _dbContext = dbContext;
        _templateCache = templateCache;
        _logger = logger;
    }

    /// <summary>
    /// Декодирует и валидирует входящее MQTT-сообщение телеметрии.
    /// </summary>
    /// <param name="context">
    /// Исходный контекст обработки с топиком и сырой полезной нагрузкой.
    /// </param>
    /// <returns>
    /// Кортеж: <c>true</c> и новый контекст с заполненными полями
    /// <see cref="MqttProcessingContext.Data"/>, <see cref="MqttProcessingContext.Device"/>
    /// и <see cref="MqttProcessingContext.Template"/> — при успехе;
    /// <c>false</c> и исходный контекст — при любой ошибке валидации или парсинга.
    /// </returns>
    public async Task<(bool Success, MqttProcessingContext UpdatedContext)> DecodeAsync(MqttProcessingContext context)
    {
        // 0. Check topic format
        // Expected format: /control/imm/{deviceId}/telemetry/
        var topicParts = context.Topic.Split('/');
        
        if (topicParts.Length != 4 || 
            topicParts[0] != "control" || 
            topicParts[1] != "imm" || 
            topicParts[3] != "telemetry")
        {
            _logger.LogError("Invalid topic format: {Topic}, parts: {topicParts}", context.Topic, topicParts);
            return (false, context);
        }

        var deviceIdString = topicParts[2];
        
        // 0a. Check if deviceId is not empty
        if (string.IsNullOrEmpty(deviceIdString))
        {
            _logger.LogError("DeviceId is empty in topic: {Topic}", context.Topic);
            return (false, context);
        }

        // Try parse device id
        if (!Guid.TryParse(deviceIdString, out Guid deviceId))
        {
            _logger.LogError("Cannot parse deviceId in topic: {Topic}, value: {deviceIdString}", context.Topic, deviceIdString);
            return (false, context);
        }

        // 1. Check payload contains valid JSON
        JsonNode payloadObject;
        try
        {
            payloadObject = JsonNode.Parse(context.Payload)!;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Payload contains invalid JSON in topic: {Topic}, payload: {Payload}", context.Topic, context.Payload);
            return (false, context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while parsing JSON in topic: {Topic}, payload: {Payload}", context.Topic, context.Payload);
            return (false, context);
        }

        if (payloadObject == null)
        {
            _logger.LogError("Payload is null or empty in topic: {Topic}", context.Topic);
            return (false, context);
        }

        // 2. Verify JSON has required top-level sections: mode, sensors, currentCycle, lastCycle
        var rootObj = payloadObject!.AsObject();

        var timestampToken = rootObj["timestamp"];
        if (timestampToken == null)
        {
            _logger.LogError("Payload does not contain 'timestamp' field in topic: {Topic}", context.Topic);
            return (false, context);
        }

        var modeToken = rootObj["mode"];
        if (modeToken == null)
        {
            _logger.LogError("Payload does not contain 'mode' field in topic: {Topic}", context.Topic);
            return (false, context);
        }
        var mode = modeToken.GetValueKind() == JsonValueKind.String
            ? modeToken.GetValue<string>() : modeToken.ToJsonString();
        var normalizedMode = ImmMode.Normalize(mode);
        if (normalizedMode is not (ImmMode.Auto or ImmMode.Manual or ImmMode.Idle or ImmMode.Alarm))
        {
            _logger.LogError("Unknown 'mode' value '{Mode}' in topic: {Topic}", mode, context.Topic);
            return (false, context);
        }

        if (!rootObj.ContainsKey("sensors") || rootObj["sensors"] is not JsonObject sensorsAsObject)
        {
            _logger.LogError("Payload does not contain 'sensors' object in topic: {Topic}", context.Topic);
            return (false, context);
        }

        if (!rootObj.ContainsKey("currentCycle") || !rootObj.ContainsKey("lastCycle"))
        {
            _logger.LogError("Payload missing 'currentCycle'/'lastCycle' section in topic: {Topic}", context.Topic);
            return (false, context);
        }

        // Build sensors dictionary: { name: { value, error } }
        var sensorsDict = new Dictionary<string, SignalValue>();
        foreach (var prop in sensorsAsObject)
        {
            if (prop.Value is not JsonObject sigObj)
                continue;
            var valueToken = sigObj["value"];
            if (valueToken == null)
                continue;
            var value = valueToken.GetValueKind() == JsonValueKind.String
                ? valueToken.GetValue<string>() : valueToken.ToJsonString();
            var error = sigObj["error"] is { } errToken && errToken.GetValueKind() == JsonValueKind.True;
            sensorsDict[prop.Key] = new SignalValue(value, error);
        }

        if (sensorsDict.Count == 0 || !sensorsDict.ContainsKey("cycleCounter"))
        {
            _logger.LogError("Sensors missing or missing reserved 'cycleCounter' key in topic: {Topic}", context.Topic);
            return (false, context);
        }

        CycleSnapshot? currentCycle = null;
        if (rootObj["currentCycle"] is JsonObject ccObj)
        {
            if (!TryParseCycleSnapshot(ccObj, out currentCycle))
            {
                _logger.LogError("Cannot parse 'currentCycle' in topic: {Topic}", context.Topic);
                return (false, context);
            }
        }

        CompletedCycleSnapshot? lastCycle = null;
        if (rootObj["lastCycle"] is JsonObject lcObj)
        {
            if (!TryParseCompletedCycleSnapshot(lcObj, out lastCycle))
            {
                _logger.LogError("Cannot parse 'lastCycle' in topic: {Topic}", context.Topic);
                return (false, context);
            }
        }

        // 5. Find and validate device exists in DB
        var immEntity = await _dbContext.Imms.FirstOrDefaultAsync(x => x.Id == deviceId);
        if (immEntity == null)
        {
            _logger.LogError("Device with Id={deviceId} not found in database for topic: {Topic}", deviceId, context.Topic);
            return (false, context);
        }

        // Create ImmDto from entity
        var immDto = new ImmDto
        {
            Id = immEntity.Id,
            Name = immEntity.Name,
            InventoryNumber = immEntity.InventoryNumber,
            TemplateId = immEntity.TemplateId,
            Manufacturer = immEntity.Template?.Manufacturer, // Get from related template
            Model = immEntity.Template?.Model,
            IsActive = immEntity.IsActive,
            CreatedAt = immEntity.CreatedAt
        };

        // 6. Find template for this imm in cache
        var cachedTemplate = _templateCache.GetById(immEntity.TemplateId);
        if (cachedTemplate == null)
        {
            _logger.LogError("Template with Id={TemplateId} not found in cache for device: {deviceId}", immEntity.TemplateId, deviceId);
            return (false, context);
        }

        // Нормализация timestamp. ISO-строка сохраняет доли секунды; число трактуется
        // как Unix-СЕКУНДЫ — обратная совместимость с продюсерами, шлющими число.
        // timestampToken объявлен как JsonNode? — на этой строке он уже проверен на null
        // выше (ранний выход «Payload does not contain 'timestamp' field»), поэтому «!».
        if (!TryParseTimestamp(timestampToken!, out var timestampUtc))
        {
            _logger.LogError("Cannot parse timestamp {TimestampValue} in topic: {Topic}",
                timestampToken.ToJsonString(), context.Topic);
            return (false, context);
        }

        var telemetryMessage = new MqttTelemetryMessage
        {
            TimestampUtc = timestampUtc,
            DeviceId = deviceId.ToString(),
            Mode = mode,
            Sensors = sensorsDict,
            CurrentCycle = currentCycle,
            LastCycle = lastCycle
        };

        return (true, context with
        {
            Data = telemetryMessage,
            Device = immDto,
            Template = cachedTemplate
        });
    }

    /// <summary>Границы диапазона, который принимает <see cref="DateTimeOffset.FromUnixTimeSeconds"/>.</summary>
    private const long MinUnixSeconds = -62135596800L;
    private const long MaxUnixSeconds = 253402300799L;

    /// <summary>
    /// Разбирает поле <c>timestamp</c>: ISO-8601 строка (доли секунды сохраняются)
    /// либо число — Unix-секунды. Результат всегда <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    /// <param name="token">Узел JSON со значением поля <c>timestamp</c>.</param>
    /// <param name="utc">Разобранный момент времени в UTC.</param>
    /// <returns><see langword="true"/> при успешном разборе; иначе <see langword="false"/>.</returns>
    private static bool TryParseTimestamp(JsonNode token, out DateTime utc)
    {
        utc = default;

        switch (token.GetValueKind())
        {
            case JsonValueKind.String:
                // AdjustToUniversal конвертирует смещение (если есть) в UTC; AssumeUniversal
                // трактует строку без указания зоны как уже-UTC (а не локальное время) —
                // все продюсеры шлют UTC, а Kind=Unspecified недопустим для timestamptz.
                // (RoundtripKind несовместим с AdjustToUniversal — кидает ArgumentException.)
                if (!DateTime.TryParse(
                        token.GetValue<string>(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var parsed))
                    return false;

                // Округляем до миллисекунды: Npgsql/timestamptz хранит с точностью до микросекунд,
                // а DateTime.TryParse сохраняет полную точность тиков (100нс) из ISO-строки — без
                // усечения сравнение (CycleNumber, StartTime) после round-trip через БД не совпадёт.
                utc = parsed.AddTicks(-(parsed.Ticks % TimeSpan.TicksPerMillisecond));
                return true;

            case JsonValueKind.Number:
                if (!token.AsValue().TryGetValue<long>(out var seconds))
                    return false;
                if (seconds < MinUnixSeconds || seconds > MaxUnixSeconds)
                    return false;
                utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseCycleSnapshot(JsonObject obj, out CycleSnapshot? snapshot)
    {
        snapshot = null;

        if (obj["number"] is not { } numberToken || !numberToken.AsValue().TryGetValue<int>(out var number))
            return false;
        if (obj["startTime"] is not { } startToken || !TryParseTimestamp(startToken, out var startTime))
            return false;

        DateTime? injectionStartTime = null;
        if (obj["injectionStartTime"] is { } injToken)
        {
            if (!TryParseTimestamp(injToken, out var inj))
                return false;
            injectionStartTime = inj;
        }

        decimal? cushion = null;
        if (obj["cushion"] is { } cushionToken)
        {
            if (!cushionToken.AsValue().TryGetValue<decimal>(out var c))
                return false;
            cushion = c;
        }

        snapshot = new CycleSnapshot(number, startTime, injectionStartTime, cushion);
        return true;
    }

    private static bool TryParseCompletedCycleSnapshot(JsonObject obj, out CompletedCycleSnapshot? snapshot)
    {
        snapshot = null;

        if (obj["number"] is not { } numberToken || !numberToken.AsValue().TryGetValue<int>(out var number))
            return false;
        if (obj["startTime"] is not { } startToken || !TryParseTimestamp(startToken, out var startTime))
            return false;
        if (obj["endTime"] is not { } endToken || !TryParseTimestamp(endToken, out var endTime))
            return false;

        DateTime? injectionStartTime = null;
        if (obj["injectionStartTime"] is { } injToken)
        {
            if (!TryParseTimestamp(injToken, out var inj))
                return false;
            injectionStartTime = inj;
        }

        decimal? cushion = null;
        if (obj["cushion"] is { } cushionToken)
        {
            if (!cushionToken.AsValue().TryGetValue<decimal>(out var c))
                return false;
            cushion = c;
        }

        snapshot = new CompletedCycleSnapshot(number, startTime, endTime, injectionStartTime, cushion);
        return true;
    }
}
