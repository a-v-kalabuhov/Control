using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.MQTT;

public interface IMessageProcessor
{
    bool Enqueue(MqttProcessingContext context);
}