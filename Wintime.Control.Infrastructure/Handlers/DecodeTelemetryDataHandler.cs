using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
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

        // 2. Verify JSON has required fields: timestamp and sensors
        var timestampToken = payloadObject?["timestamp"];
        var sensorsToken = payloadObject?["sensors"];
        var modeToken = payloadObject?["mode"];

        if (timestampToken == null)
        {
            _logger.LogError("Payload does not contain 'timestamp' field in topic: {Topic}", context.Topic);
            return (false, context);
        }

        if (modeToken == null)
        {
            _logger.LogError("Payload does not contain 'mode' field in topic: {Topic}", context.Topic);
            return (false, context);
        }
        var mode = modeToken.GetValueKind() == JsonValueKind.String 
            ? modeToken.GetValue<string>() : modeToken.ToJsonString();

        if (sensorsToken == null)
        {
            _logger.LogError("Payload does not contain 'sensors' field in topic: {Topic}", context.Topic);
            return (false, context);
        }

        // 3. Verify sensors array is not empty
        var sensorsAsObject = sensorsToken.AsObject();
        if ((sensorsAsObject == null) || (sensorsAsObject.Count == 0))
        {
            _logger.LogError("Sensors array is empty in topic: {Topic}", context.Topic);
            return (false, context);
        }

        // Build sensors dictionary from JSON
        var sensorsDict = new Dictionary<string, string>();
        foreach(var prop in sensorsAsObject)
        {
            if(prop.Value != null)
            {
                sensorsDict[prop.Key] = prop.Value.GetValueKind() == JsonValueKind.String
                    ? prop.Value.GetValue<string>()
                    : prop.Value.ToJsonString();
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

        // Attempt to extract and convert timestamp
        try
        {
            // If timestamp is ISO string, convert to Unix timestamp
            if (timestampToken.GetValueKind() == JsonValueKind.String)
            {
                string timestampStr = timestampToken.ToString();
                DateTime timestamp;
                if (DateTime.TryParse(timestampStr, out timestamp))
                {
                    var unixTimestamp = ((DateTimeOffset) timestamp).ToUnixTimeSeconds();
                    
                    // Create MqttTelemetryMessage object with data from payload
                    var existingData = context.Data ?? new MqttTelemetryMessage
                    {
                        Timestamp = unixTimestamp,
                        DeviceId = deviceId.ToString(),
                        Mode = mode,
                        Sensors = sensorsDict
                    };

                    // Return a new context with updated data, keeping other properties intact
                    var newContext = new MqttProcessingContext(
                        context.MessageId,
                        context.Topic,
                        context.Payload,
                        existingData,
                        immDto,
                        cachedTemplate
                    );
                    
                return (true, newContext); 
                }
                else
                {
                    _logger.LogError("Cannot parse timestamp value: {timestampStr} in topic: {Topic}", timestampStr, context.Topic);
                    return (false, context);
                }
            }
            else if (timestampToken.GetValueKind() == JsonValueKind.Number)
            {
                // If timestamp is already a Unix timestamp (number)
                var unixTimestamp = timestampToken.GetValue<long>();
                
                // Create MqttTelemetryMessage object with data from payload
                var existingData = context.Data ?? new MqttTelemetryMessage
                {
                    Timestamp = unixTimestamp,
                    DeviceId = deviceId.ToString(),
                    Sensors = sensorsDict
                };
                
                var newTelemetryMessage = new MqttTelemetryMessage
                {
                    Timestamp = unixTimestamp,
                    DeviceId = deviceId.ToString(),
                    Mode = mode,
                    Sensors = sensorsDict
                };
                
                // Since records are immutable, we return success (true), 
                // and expect MessageProcessingPipeline to update context
                // by calling context with { Data = newTelemetryMessage, Device = immDto, Template = cachedTemplate }
                // upon return from DecodeAsync when true is returned.
                
                return (true, context with 
                {
                    Data = newTelemetryMessage,
                    Device = immDto,
                    Template = cachedTemplate
                }); 
            }
            else
            {
                _logger.LogError("Timestamp field has invalid type in topic: {Topic}", context.Topic);
                return (false, context);
            }
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Error while processing timestamp in topic: {Topic}, value: {TimestampValue}", context.Topic, timestampToken);
            return (false, context);
        }
    }
}
