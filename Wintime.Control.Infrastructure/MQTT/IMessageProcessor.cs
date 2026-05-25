using Wintime.Control.SDK.Mqtt;

namespace Wintime.Control.Infrastructure.Mqtt;

public interface IMessageProcessor
{
    bool Enqueue(MqttMessage message);
}