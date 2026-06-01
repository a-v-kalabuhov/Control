using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Packets;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;
using Wintime.Control.Infrastructure.Mqtt;

namespace Wintime.Control.Infrastructure.MQTT;

/// <summary>
/// Реализация сервиса для работы с MQTT-брокером через библиотеку MQTTnet.
/// Обеспечивает подключение, подписку на топики, обработку входящих сообщений,
/// автоматическое восстановление соединения и логирование событий.
/// </summary>
/// <remarks>
/// Поддерживает три топика: <c>/telemetry</c>, <c>/events</c>, <c>/status</c>.
/// Сообщения из топика <c>/telemetry</c> и <c>/status</c> обрабатываются одинаково.
/// Сообщения из топика <c>/events</c> обрабатываются отдельно и сохраняются в БД.
/// </remarks>
public class MqttService : IMqttService, IDisposable
{
    private readonly IMqttClient _mqttClient;
    private readonly MqttSettings _settings;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageProcessor _messageProcessor;
    private readonly ILogger<MqttService> _logger;
    private bool _isConnected;
    private int _isReconnecting; // 0 = idle, 1 = reconnecting (Interlocked)
    private CancellationTokenSource? _reconnectCts;
    private MqttClientOptions? _mqttOptions;

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="MqttService"/>.
    /// </summary>
    /// <param name="mqttClient">Клиент MQTT, реализующий <see cref="IMqttClient"/>.</param>
    /// <param name="settings">Настройки MQTT, полученные через <see cref="IOptions{MqttSettings}"/>.</param>
    /// <param name="serviceProvider">Провайдер зависимостей для создания локальных контекстов.</param>
    /// <param name="messageProcessor">Обработчик входящих сообщений через очередь.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public MqttService(
        IMqttClient mqttClient,
        IOptions<MqttSettings> settings,
        IServiceProvider serviceProvider,
        IMessageProcessor messageProcessor,
        ILogger<MqttService> logger)
    {
        _mqttClient = mqttClient;
        _settings = settings.Value;
        _serviceProvider = serviceProvider;
        _messageProcessor = messageProcessor;
        _logger = logger;
        _isConnected = false;

        CreateMqttClientOptions();

        _mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            await OnMessageReceivedAsync(e);
        };

        _mqttClient.DisconnectedAsync += async e =>
        {
            _isConnected = false;
            _logger.LogWarning("MQTT disconnected: {Reason}", e.Reason);
            await TryReconnect();
        };
    }

    public bool IsConnected => _isConnected;

    /// <summary>
    /// Создаёт и сохраняет параметры подключения к MQTT-брокеру в объект <see cref="MqttClientOptions"/>.
    /// </summary>
    private void CreateMqttClientOptions()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerUrl, _settings.Port)
            .WithClientId(_settings.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

        if (!string.IsNullOrEmpty(_settings.Username))
        {
            builder.WithCredentials(_settings.Username, _settings.Password ?? string.Empty);
        }

        _mqttOptions = builder.Build();
    }

    /// <summary>
    /// Подключается к MQTT-брокеру и подписывается на заданные топики.
    /// При ошибке подключения запускается автоматическая попытка переподключения.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию подключения.</returns>
    public async System.Threading.Tasks.Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_mqttClient.IsConnected)
            return;

        try
        {
            await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
            _isConnected = true;
            _logger.LogInformation("MQTT connected to {Broker}", _settings.BrokerUrl);

            await SubscribeToTopics(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT connection failed");
            await TryReconnect();
        }
    }

    /// <summary>
    /// Разрывает соединение с MQTT-брокером и отменяет попытки переподключения.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную операцию отключения.</returns>
    public async System.Threading.Tasks.Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();
        await _mqttClient.DisconnectAsync();
        _isConnected = false;
        _logger.LogInformation("MQTT disconnected by request");
    }

    /// <summary>
    /// Подписывается на топики: /telemetry, /events, /status.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию подписки.</returns>
    private async System.Threading.Tasks.Task SubscribeToTopics(CancellationToken cancellationToken)
    {
        var topics = new[]
        {
            _settings.Topics.Telemetry,
            _settings.Topics.Events,
            _settings.Topics.Status
        };

        var topicFilters = new List<MqttTopicFilter>
        {
            new MqttTopicFilterBuilder().WithTopic(_settings.Topics.Telemetry).WithAtMostOnceQoS().Build(),
            new MqttTopicFilterBuilder().WithTopic(_settings.Topics.Events).WithAtMostOnceQoS().Build(),
            new MqttTopicFilterBuilder().WithTopic(_settings.Topics.Status).WithAtMostOnceQoS().Build()
        };

        var options = new MqttClientSubscribeOptions
        {
            TopicFilters = topicFilters,
        };

        await _mqttClient.SubscribeAsync(options, cancellationToken);
        foreach (var topic in topics)
        {
            _logger.LogInformation("Subscribed to topic: {Topic}", topic);
        }
    }

    /// <summary>
    /// Пытается автоматически переподключиться к брокеру при потере соединения.
    /// Повторяет попытки с интервалом, заданным в <see cref="MqttSettings.ReconnectDelaySeconds"/>.
    /// </summary>
    /// <returns>Задача, представляющая асинхронную попытку переподключения.</returns>
    private async System.Threading.Tasks.Task TryReconnect()
    {
        // Ensure only one reconnect loop runs at a time (thread-safe via Interlocked).
        if (Interlocked.CompareExchange(ref _isReconnecting, 1, 0) != 0)
            return;

        _reconnectCts = new CancellationTokenSource();

        try
        {
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
        }
        finally
        {
            _reconnectCts = null;
            Interlocked.Exchange(ref _isReconnecting, 0);
        }
    }

    /// <summary>
    /// Обрабатывает входящее MQTT-сообщение, определяет тип по топику и делегирует обработку.
    /// </summary>
    /// <param name="e">Аргументы события получения сообщения.</param>
    /// <returns>Задача, представляющая асинхронную обработку.</returns>
    private async System.Threading.Tasks.Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            _logger.LogDebug("MQTT Message received. Topic: {Topic}, Payload: {Payload}", topic, payload);

            if (topic.Contains("/telemetry"))
            {
                await ProcessTelemetry(topic, payload);
            }
            else if (topic.Contains("/events"))
            {
                // TODO : Убрать, как и статус. Оставить только телеметрию.
                await ProcessEvent(payload);
            }
            else if (topic.Contains("/status"))
            {
                await ProcessStatus(topic, payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing MQTT message");
        }
    }

    /// <summary>
    /// Обрабатывает сообщение телеметрии: парсит JSON, извлекает DeviceId из топика,
    /// и ставит сообщение в очередь на дальнейшую обработку.
    /// </summary>
    /// <param name="topic">Топик сообщения.</param>
    /// <param name="payload">Тело сообщения в виде строки UTF-8.</param>
    /// <returns>Задача, представляющая асинхронную обработку.</returns>
    private async System.Threading.Tasks.Task ProcessTelemetry(string topic, string payload)
    {
        var context = new MqttProcessingContext(Guid.NewGuid(), topic, payload, null, null, null);
        if (!_messageProcessor.Enqueue(context))
        {
            _logger.LogWarning("Dropped message {MessageId} - queue full", context.MessageId);
        }
    }

    /// <summary>
    /// Обрабатывает сообщение события: сохраняет его в базу данных.
    /// </summary>
    /// <param name="payload">Тело сообщения в виде строки UTF-8.</param>
    /// <returns>Задача, представляющая асинхронную обработку.</returns>
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
            _logger.LogWarning("IMM not found for DeviceId: {DeviceId}", message.DeviceId);
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

        _logger.LogInformation("Event saved: {EventType} for IMM {ImmId}", eventType, imm.Id);
    }

    /// <summary>
    /// Обрабатывает сообщение статуса как телеметрию (дублирует вызов <see cref="ProcessTelemetry"/>).
    /// </summary>
    /// <param name="topic">Топик сообщения.</param>
    /// <param name="payload">Тело сообщения в виде строки UTF-8.</param>
    /// <returns>Задача, представляющая асинхронную обработку.</returns>
    private async System.Threading.Tasks.Task ProcessStatus(string topic, string payload)
    {
        // Статус обрабатывается как часть телеметрии
        await ProcessTelemetry(topic, payload);
    }

    /// <summary>
    /// Преобразует строковое представление типа события в перечисление <see cref="EventType"/>.
    /// </summary>
    /// <param name="eventType">Строковое значение типа события.</param>
    /// <returns>Соответствующее значение <see cref="EventType"/>; по умолчанию — <see cref="EventType.Downtime"/>.</returns>
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

