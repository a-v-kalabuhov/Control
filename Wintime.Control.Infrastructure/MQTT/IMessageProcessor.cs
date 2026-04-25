using Wintime.Control.Core.DTOs.Mqtt;

namespace Wintime.Control.Infrastructure.Mqtt;

public interface IMessageProcessor
{
    bool Enqueue(MqttProcessingContext context);
}