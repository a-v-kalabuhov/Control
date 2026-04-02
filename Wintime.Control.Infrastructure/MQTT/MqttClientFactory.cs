using MQTTnet;

namespace Wintime.Control.Infrastructure.MQTT;

public interface IWintimeMqttClientFactory
{
    IMqttClient CreateClient();
}

public class WintimeMqttClientFactory : IWintimeMqttClientFactory
{
    public IMqttClient CreateClient()
    {
        var factory = new MqttClientFactory();
        return factory.CreateMqttClient();
    }
}