using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;

namespace Wintime.Control.Infrastructure.MQTT;

public class MqttService : IMqttService, IDisposable
{
    private readonly IMqttClient _mqttClient;
    private readonly MqttSettings _settings;
    private readonly ICovFilter _covFilter;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MqttService> _logger;
    private readonly Dictionary<string, SensorThreshold> _thresholds;
    private bool _isConnected;
    private CancellationTokenSource? _reconnectCts;

    public MqttService(
        IMqttClient mqttClient,
        IOptions<MqttSettings> settings,
        ICovFilter covFilter,
        IServiceProvider serviceProvider,
        ILogger<MqttService> logger,
        Dictionary<string, SensorThreshold> thresholds)
    {
        _mqttClient = mqttClient;
        _settings = settings.Value;
        _covFilter = covFilter;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _thresholds = thresholds;
        _isConnected = false;

        SetupMqttClientHandlers();
    }

    public bool IsConnected => _isConnected;

    private void SetupMqttClientHandlers()
    {
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceived;
        _mqttClient.ConnectedAsync += e =>
        {
            _logger.LogInformation("✅ MQTT connected to {Broker}", _settings.BrokerUrl);
            _isConnected = true;
            return System.Threading.Tasks.Task.CompletedTask;
        };
        _mqttClient.DisconnectedAsync += async e =>
        {
            _logger.LogWarning("⚠️ MQTT disconnected. Reason: {Reason}", e.Reason);
            _isConnected = false;
            await TryReconnect();
        };
    }

    public async System.Threading.Tasks.Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerUrl, _settings.Port)
            .WithClientId(_settings.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(_settings.Username))
        {
            options.WithCredentials(_settings.Username, _settings.Password ?? string.Empty);
        }

        try
        {
            await _mqttClient.ConnectAsync(options.Options, cancellationToken);
            await SubscribeToTopics(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ MQTT connection failed");
            await TryReconnect();
        }
    }

    public async System.Threading.Tasks.Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();
        await _mqttClient.DisconnectAsync();
        _isConnected = false;
        _logger.LogInformation("MQTT disconnected by request");
    }

    private async System.Threading.Tasks.Task SubscribeToTopics(CancellationToken cancellationToken)
    {
        var topics = new[]
        {
            _settings.Topics.Telemetry,
            _settings.Topics.Events,
            _settings.Topics.Status
        };

        foreach (var topic in topics)
        {
            await _mqttClient.SubscribeAsync(topic, cancellationToken);
            _logger.LogInformation("📡 Subscribed to topic: {Topic}", topic);
        }
    }

    private async System.Threading.Tasks.Task TryReconnect()
    {
        if (_reconnectCts != null)
            return; // Уже идёт попытка переподключения

        _reconnectCts = new CancellationTokenSource();

        while (!_isConnected && !_reconnectCts.Token.IsCancellationRequested)
        {
            _logger.LogInformation("🔄 Trying to reconnect in {Seconds} seconds...", _settings.ReconnectDelaySeconds);
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(_settings.ReconnectDelaySeconds), _reconnectCts.Token);

            try
            {
                await ConnectAsync(_reconnectCts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconnection failed");
            }
        }

        _reconnectCts = null;
    }

    private async System.Threading.Tasks.Task OnMessageReceived(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            _logger.LogDebug("📨 MQTT Message received. Topic: {Topic}, Payload: {Payload}", topic, payload);

            if (topic.Contains("/telemetry"))
            {
                await ProcessTelemetry(payload);
            }
            else if (topic.Contains("/events"))
            {
                await ProcessEvent(payload);
            }
            else if (topic.Contains("/status"))
            {
                await ProcessStatus(payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
        }
    }

    private async System.Threading.Tasks.Task ProcessTelemetry(string payload)
    {
        var message = JsonSerializer.Deserialize<MqttTelemetryMessage>(payload);
        if (message == null || string.IsNullOrEmpty(message.DeviceId))
            return;

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var imm = await context.Imms
            .Include(i => i.Template)
            .FirstOrDefaultAsync(i => i.Name == message.DeviceId || i.InventoryNumber == message.DeviceId);

        if (imm == null)
        {
            _logger.LogWarning("⚠️ IMM not found for DeviceId: {DeviceId}", message.DeviceId);
            return;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(message.Ts).DateTime;

        // Обрабатываем каждый параметр через COV-фильтр
        var parametersToSave = new List<(string Name, decimal? Numeric, string? Text)>();

        if (message.Data.Status != null)
        {
            if (_covFilter.ShouldSave(imm.Id.ToString(), "status", message.Data.Status, timestamp))
                parametersToSave.Add(("status", null, message.Data.Status));
        }

        if (message.Data.Cycles.HasValue)
        {
            if (_covFilter.ShouldSave(imm.Id.ToString(), "cycles", message.Data.Cycles.Value, timestamp))
                parametersToSave.Add(("cycles", message.Data.Cycles.Value, null));
        }

        if (message.Data.CycleTime.HasValue)
        {
            if (_covFilter.ShouldSave(imm.Id.ToString(), "cycle_time", message.Data.CycleTime.Value, timestamp))
                parametersToSave.Add(("cycle_time", message.Data.CycleTime.Value, null));
        }

        if (message.Data.TempZone1.HasValue)
        {
            if (_covFilter.ShouldSave(imm.Id.ToString(), "temp_zone_1", message.Data.TempZone1.Value, timestamp))
                parametersToSave.Add(("temp_zone_1", message.Data.TempZone1.Value, null));
        }

        if (message.Data.PressureInject.HasValue)
        {
            if (_covFilter.ShouldSave(imm.Id.ToString(), "pressure_inject", message.Data.PressureInject.Value, timestamp))
                parametersToSave.Add(("pressure_inject", message.Data.PressureInject.Value, null));
        }

        // Сохраняем отфильтрованные данные
        if (parametersToSave.Any())
        {
            var telemetryRecords = parametersToSave.Select(p => new Telemetry
            {
                ImmId = imm.Id,
                Timestamp = timestamp,
                ParameterName = p.Name,
                ValueNumeric = p.Numeric,
                ValueText = p.Text
            }).ToList();

            await context.Telemetry.AddRangeAsync(telemetryRecords);
            await context.SaveChangesAsync();

            _logger.LogDebug("💾 Saved {Count} telemetry records for IMM {ImmId}", parametersToSave.Count, imm.Id);
        }
    }

    private async System.Threading.Tasks.Task ProcessEvent(string payload)
    {
        var message = JsonSerializer.Deserialize<MqttEventMessage>(payload);
        if (message == null || string.IsNullOrEmpty(message.DeviceId))
            return;

        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var imm = await context.Imms
            .FirstOrDefaultAsync(i => i.Name == message.DeviceId || i.InventoryNumber == message.DeviceId);

        if (imm == null)
        {
            _logger.LogWarning("⚠️ IMM not found for DeviceId: {DeviceId}", message.DeviceId);
            return;
        }

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(message.Ts).DateTime;
        var eventType = MapEventType(message.EventType);

        var evt = new Event
        {
            ImmId = imm.Id,
            EventType = eventType,
            ErrorCode = message.Payload.Code,
            ErrorMessage = message.Payload.Message,
            StartTime = timestamp,
            EndTime = eventType == EventType.CycleComplete || eventType == EventType.CycleAborted 
                ? timestamp 
                : null
        };

        context.Events.Add(evt);
        await context.SaveChangesAsync();

        _logger.LogInformation("📝 Event saved: {EventType} for IMM {ImmId}", eventType, imm.Id);
    }

    private async System.Threading.Tasks.Task ProcessStatus(string payload)
    {
        // Статус обрабатывается как часть телеметрии
        await ProcessTelemetry(payload);
    }

    private static EventType MapEventType(string eventType)
    {
        return eventType switch
        {
            "alarm_start" => EventType.Alarm,
            "alarm_end" => EventType.Alarm,
            "cycle_complete" => EventType.CycleComplete,
            "cycle_aborted" => EventType.CycleAborted,
            "downtime_start" => EventType.Downtime,
            "downtime_end" => EventType.Downtime,
            "setup_start" => EventType.Setup,
            "setup_end" => EventType.Setup,
            _ => EventType.Downtime
        };
    }

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _mqttClient.Dispose();
    }
}