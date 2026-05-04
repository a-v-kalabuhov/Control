namespace Wintime.Control.Emulator.Services;

using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;


public interface IEmulatorMqttService
{
    Task ConnectAsync(CancellationToken ct);
    Task PublishAsync(string immId, TelemetryPayload payload, CancellationToken ct);
}

public class EmulatorMqttService : IEmulatorMqttService
{
    private readonly IMqttClient _client;
    private readonly MqttSettings _settings;
    private readonly ILogger<EmulatorMqttService> _logger;

    public EmulatorMqttService(IOptions<EmulatorSettings> settings, ILogger<EmulatorMqttService> logger)
    {
        _settings = settings.Value.Mqtt;
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerUrl.Replace("mqtt://", "").Split(':')[0], 
                           int.Parse(_settings.BrokerUrl.Split(':')[^1]))
            .WithClientId($"{_settings.ClientIdPrefix}_{Guid.NewGuid()}")
            .Build();

        await _client.ConnectAsync(options, ct);
        _logger.LogInformation("MQTT connected to {Broker}", _settings.BrokerUrl);
    }

    public async Task PublishAsync(string immId, TelemetryPayload payload, CancellationToken ct)
    {
        var topic = _settings.TopicTemplate.Replace("{immId}", immId);
        var json = JsonSerializer.Serialize(payload);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.PublishAsync(message, ct);
    }
}
