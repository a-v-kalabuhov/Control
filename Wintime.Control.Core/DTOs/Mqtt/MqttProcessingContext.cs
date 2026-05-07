using Wintime.Control.Core.Cache;
using Wintime.Control.Core.DTOs.Imm;

namespace Wintime.Control.Core.DTOs.Mqtt;

public record MqttProcessingContext(
    Guid MessageId,
    string Topic,
    string Payload,
    MqttTelemetryMessage? Data,
    ImmDto? Device,
    CachedTemplate? Template
);
