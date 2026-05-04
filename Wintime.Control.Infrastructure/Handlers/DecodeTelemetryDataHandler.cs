using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Text.Json;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.DTOs.Template;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Handlers;

public class DecodeTelemetryDataHandler : IDecodeTelemetryDataHandler
{
    private readonly ControlDbContext _dbContext;
    private readonly ILogger<DecodeTelemetryDataHandler> _logger;
    
    public DecodeTelemetryDataHandler(ControlDbContext dbContext, ILogger<DecodeTelemetryDataHandler> logger)
    {
        _dbContext = dbContext;
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
        var sensorsDict = new Dictionary<string, object>();
        foreach(var prop in sensorsAsObject)
        {
            if(prop.Value != null)
            {
                sensorsDict[prop.Key] = prop.Value.GetValue<object>();
            }
        }
        
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

        // 6. Find template for this imm
        var templateEntity = await _dbContext.Templates.FirstOrDefaultAsync(x => x.Id == immEntity.TemplateId);
        if (templateEntity == null)
        {
            _logger.LogError("Template with Id={TemplateId} not found in database for device: {deviceId}", immEntity.TemplateId, deviceId);
            return (false, context);
        }

        // Create TemplateDto from entity
        var templateDto = new TemplateDto
        {
            Id = templateEntity.Id,
            Name = templateEntity.Name,
            Manufacturer = templateEntity.Manufacturer,
            Model = templateEntity.Model,
            Version = templateEntity.Version,
            Author = templateEntity.Author,
            JsonConfig = templateEntity.JsonConfig,
            CreatedAt = templateEntity.CreatedAt
        };

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
                        templateDto
                    );
                    
                    // Since we're returning a record (struct-like),
                    // we need to assign to the original context in some way
                    // Actually, the pipeline pattern implies that the handler transforms the context
                    // But our interface signature returns bool
                    
                    // Update context properties for next handlers in pipeline
                    // We need to update context outside this method, so the return false/true means continue/stop
                    // For struct records like MqttProcessingContext, we would need to pass back a modified instance
                    // Since we can't do that with the current interface, the pipeline itself should handle updating the context
                    
                    // So update context via the method parameters would be by returning the result
                    // Since the interface uses boolean, it implies the next handler in the chain will modify the object directly
                    // In our case, update the MqttTelemetryMessage and set to context.Data, immDto to context.Device, templateDto to context.Template
                    // We can only make changes via the context we have access to. If the context was a class instead of record, 
                    // we could modify it directly. Since it's a record, changes need to be propagated differently.
                    // Let's update the context directly via a custom approach - though in practice, the method signature may need adjustment
        
                    // Update the passed-in context Data, Device and Template properties
                    // Note: Records are immutable, so we can't directly set props
                    // The pipeline class uses a `with` expression to create new instances
                    // So the return of success will allow the pipeline to create a new context with updated data 
                    // while maintaining the same id, topic, and payload
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
                
                // Since our Decode method returns bool, not modified context
                // we need to somehow update the original context.
                // Looking again at the pipeline behavior - it says:
                // "if (!await decoder.DecodeAsync(context)) return;"
                // This suggests a 'false' stops processing but 'true' continues
                // And then the context.Data, .Device, etc. might need to be set in the handler itself
                // by returning a new context or setting some out parameter

                // Actually, let me look at the implementation again:
                // It seems like the pipeline expects the handler to update context directly or the context is meant to be mutable
                // Or that with expression is used to update: context with {Data=newData,...}

                // The actual solution: the context object is immutable (record), so individual properties can't be mutated
                // The Decode method would need to return the modified context to the caller (pipeline)
                // But it's bool according to interface.
                // Given the comment in MessageProcessingPipeline:
                // "На момент начала обработки context не заполнен полностью, заполнены только поля Topic и Payload..."

                // This means our handler needs to update internal state.
                // Perhaps we should change the implementation strategy by updating the record properties
                // and using reflection or specific technique to modify immutable record.
                // However in pipeline, maybe the approach is:
                // var newContext = context with {Data=updatedMessage, Device=device, Template=template};

                // Looking at pipeline again:
                // var decoder = _sp.GetRequiredService<IDecodeTelemetryDataHandler>();
                // if (!await decoder.DecodeAsync(context))
                //     return;

                // So this means the decoder doesn't return modified context - only success/failure!
                // So the decoder needs to modify the context internally or use ref/out parameter or closure
                
                // Since Decode method interface has only bool - it indicates success/failure only.
                // So it seems we need direct access to modify context or there's a different mechanism.
                // Since it's a struct-like record with immutable props, it would need different handling.
                // The solution probably involves the pipeline accepting the returned context.

                // Actually, looking closely, MqttProcessingContext is a record with a primary constructor,
                // meaning the fields are mutable in the way used by "with".
                // I think the intent for the DecodeAsync is:
                // 1. If decode/validation succeeds: return true AND the context is appropriately modified
                // 2. If validation fails: write error info and return false to stop pipeline
                
                // However, the context record being immutable means we cannot modify its members during the method,
                // instead, the pipeline should update the record with new values after calling DecodeAsync.
                
                // Maybe the context is accessed by ref somehow in calling code?
                var newTelemetryMessage = new MqttTelemetryMessage
                {
                    Timestamp = unixTimestamp,
                    DeviceId = deviceId.ToString(),
                    Sensors = sensorsDict
                };
                
                // Since records are immutable, we return success (true), 
                // and expect MessageProcessingPipeline to update context
                // by calling context with { Data = newTelemetryMessage, Device = immDto, Template = templateDto }
                // upon return from DecodeAsync when true is returned.
                
                return (true, context with 
                {
                    Data = newTelemetryMessage,
                    Device = immDto,
                    Template = templateDto
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
