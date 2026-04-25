using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Template;

namespace Wintime.Control.Core.DTOs.Mqtt;

public record MqttProcessingContext(
    Guid MessageId,
    string Topic,
    string Payload, 
    MqttTelemetryMessage? Data,
    ImmDto? Device, 
    TemplateDto? Template
);