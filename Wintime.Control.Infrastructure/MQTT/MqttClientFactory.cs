using MQTTnet;

namespace Wintime.Control.Infrastructure.MQTT;

public interface IMqttClientFactory
{
    IMqttClient CreateClient();
}

public class MqttClientFactory : IMqttClientFactory
{
    public IMqttClient CreateClient()
    {
        var factory = new MqttFactory();
        return factory.CreateMqttClient();
    }
}