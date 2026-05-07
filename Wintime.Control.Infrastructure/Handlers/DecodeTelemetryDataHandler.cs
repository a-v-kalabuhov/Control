using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Handlers;

public class DecodeTelemetryDataHandler : IDecodeTelemetryDataHandler
{
    private readonly ControlDbContext _dbContext;
    private readonly ITemplateCache _templateCache;
    private readonly ILogger<DecodeTelemetryDataHandler> _logger;

    public DecodeTelemetryDataHandler(
        ControlDbContext dbContext,
        ITemplateCache templateCache,
        ILogger<DecodeTelemetryDataHandler> logger)
    {
        _dbContext = dbContext;
        _templateCache = templateCache;
        _logger = logger;
    }

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

        if (timestampToken == null)
        {
            _logger.LogError("Payload does not contain 'timestamp' field in topic: {Topic}", context.Topic);
            return (false, context);
        }

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
                sensorsDict[prop.Key] = prop.Value.GetValue<string>();
            }
        }
        
        // TODO : нужно будет убрать эту проверку отсюда. Для шаблона датчика нужно ввести признак обязательности. Тогда надо будет проверять наичие всех ообязательных значений. Сейчас это получается hardcode.
        // 4. Check for mandatory sensor values: counter and mode
        if (!sensorsDict.ContainsKey("counter"))
        {
            _logger.LogError("Sensors array does not contain mandatory 'counter' field in topic: {Topic}", context.Topic);
            return (false, context);
        }

        if (!sensorsDict.ContainsKey("mode"))
        {
            _logger.LogError("Sensors array does not contain mandatory 'mode' field in topic: {Topic}", context.Topic);
            return (false, context);
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
