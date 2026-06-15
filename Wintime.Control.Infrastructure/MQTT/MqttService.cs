using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Packets;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Shared.Settings;

namespace Wintime.Control.Infrastructure.MQTT;

/// <summary>
/// Реализация сервиса для работы с MQTT-брокером через библиотеку MQTTnet.
/// Обеспечивает подключение, подписку на топики, обработку входящих сообщений,
/// автоматическое восстановление соединения и логирование событий.
/// </summary>
/// <remarks>
/// Подписывается только на топик <c>/telemetry</c>. События (аварии, cycle_complete)
/// и статусы ТПА определяются в хендлерах, обрабатывающих телеметрию, поэтому отдельные
/// каналы <c>/events</c> и <c>/status</c> не используются.
/// </remarks>
public class MqttService : IMqttService, IDisposable
{
    private readonly IMqttClient _mqttClient;
    private readonly MqttSettings _settings;
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
    /// <param name="messageProcessor">Обработчик входящих сообщений через очередь.</param>
    /// <param name="logger">Логгер для записи событий.</param>
    public MqttService(
        IMqttClient mqttClient,
        IOptions<MqttSettings> settings,
        IMessageProcessor messageProcessor,
        ILogger<MqttService> logger)
    {
        _mqttClient = mqttClient;
        _settings = settings.Value;
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
    /// Подписывается на топик /telemetry.
    /// </summary>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Задача, представляющая асинхронную операцию подписки.</returns>
    private async System.Threading.Tasks.Task SubscribeToTopics(CancellationToken cancellationToken)
    {
        var topicFilters = new List<MqttTopicFilter>
        {
            new MqttTopicFilterBuilder().WithTopic(_settings.Topics.Telemetry).WithAtMostOnceQoS().Build()
        };

        var options = new MqttClientSubscribeOptions
        {
            TopicFilters = topicFilters,
        };

        await _mqttClient.SubscribeAsync(options, cancellationToken);
        _logger.LogInformation("Subscribed to topic: {Topic}", _settings.Topics.Telemetry);
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

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _mqttClient.Dispose();
    }
}

