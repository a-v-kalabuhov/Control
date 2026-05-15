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
    private MqttClientOptions? _options;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    public EmulatorMqttService(IOptions<EmulatorSettings> settings, ILogger<EmulatorMqttService> logger)
    {
        _settings = settings.Value.Mqtt;
        _logger = logger;
        _client = new MqttFactory().CreateMqttClient();

        _client.DisconnectedAsync += async e =>
        {
            _logger.LogWarning("MQTT disconnected: {Reason}. Reconnecting...", e.Reason);
            await TryReconnectAsync(CancellationToken.None);
        };
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerUrl.Replace("mqtt://", "").Split(':')[0],
                           int.Parse(_settings.BrokerUrl.Split(':')[^1]))
            .WithClientId($"{_settings.ClientIdPrefix}_{Guid.NewGuid()}")
            .Build();

        await _client.ConnectAsync(_options, ct);
        _logger.LogInformation("MQTT connected to {Broker}", _settings.BrokerUrl);
    }

    public async Task PublishAsync(string immId, TelemetryPayload payload, CancellationToken ct)
    {
        if (!_client.IsConnected)
            await TryReconnectAsync(ct);

        var topic = _settings.TopicTemplate.Replace("{immId}", immId);
        var json = JsonSerializer.Serialize(payload);
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.PublishAsync(message, ct);
    }

    private async Task TryReconnectAsync(CancellationToken ct)
    {
        if (_options is null) return;
        if (!await _reconnectLock.WaitAsync(0)) return; // уже идёт переподключение
        try
        {
            while (!_client.IsConnected && !ct.IsCancellationRequested)
            {
                try
                {
                    await _client.ConnectAsync(_options, ct);
                    _logger.LogInformation("MQTT reconnected to {Broker}", _settings.BrokerUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("MQTT reconnect failed: {Message}. Retrying in 5s...", ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }
        finally
        {
            _reconnectLock.Release();
        }
    }
}
