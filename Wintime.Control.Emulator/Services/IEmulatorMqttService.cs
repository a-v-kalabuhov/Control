namespace Wintime.Control.Emulator.Services;

using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using System.Text.Json;
using System.Threading.Channels;
using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;


public interface IEmulatorMqttService
{
    Task ConnectAsync(CancellationToken ct);
    Task PublishAsync(string immId, TelemetryPayload payload, CancellationToken ct);
}

/// <summary>
/// Один общий <see cref="IMqttClient"/> на все инстансы эмуляции. MQTTnet не допускает
/// параллельные QoS-1 публикации из разных потоков: одновременные запросы берут один и тот
/// же packet identifier, что ломает диспетчер пакетов (timeout / "unexpected PubAck").
///
/// Поэтому используется модель producer–consumer: инстансы (продюсеры) кладут сообщения в
/// канал через <see cref="PublishAsync"/>, а единственный фоновый consumer вычитывает их по
/// одному и отправляет по сети. Так публикации естественным образом сериализуются.
/// </summary>
public class EmulatorMqttService : IEmulatorMqttService, IAsyncDisposable
{
    private readonly record struct PublishItem(string ImmId, TelemetryPayload Payload);

    private readonly IMqttClient _client;
    private readonly MqttSettings _settings;
    private readonly ILogger<EmulatorMqttService> _logger;
    private MqttClientOptions? _options;
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);

    // Очередь телеметрии. Bounded + DropOldest: телеметрию не жалко потерять, продюсеры
    // никогда не блокируются, а память не растёт, если брокер недоступен.
    private readonly Channel<PublishItem> _channel = Channel.CreateBounded<PublishItem>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly CancellationTokenSource _consumerCts = new();
    private Task? _consumerTask;

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

        _consumerTask ??= Task.Run(() => ConsumeLoopAsync(_consumerCts.Token));
    }

    /// <summary>
    /// Ставит сообщение в очередь на отправку. Не блокирует и не бросает исключений:
    /// при переполнении очереди вытесняется самое старое сообщение.
    /// </summary>
    public Task PublishAsync(string immId, TelemetryPayload payload, CancellationToken ct)
    {
        _channel.Writer.TryWrite(new PublishItem(immId, payload));
        return Task.CompletedTask;
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(ct))
        {
            try
            {
                if (!_client.IsConnected)
                    await TryReconnectAsync(ct);

                var topic = _settings.TopicTemplate.Replace("{immId}", item.ImmId);
                var json = JsonSerializer.Serialize(item.Payload);
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(json)
                    .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build();

                await _client.PublishAsync(message, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Одно сообщение телеметрии потерять не страшно — следующее придёт на
                // очередном тике. Главное — не уронить consumer.
                _logger.LogWarning(ex, "Publish error for {ImmId}, message dropped", item.ImmId);
            }
        }
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

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _consumerCts.CancelAsync();
        if (_consumerTask is not null)
        {
            try { await _consumerTask; }
            catch (OperationCanceledException) { }
        }
        _consumerCts.Dispose();
        _client.Dispose();
    }
}
