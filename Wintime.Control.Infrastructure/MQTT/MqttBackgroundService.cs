using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Wintime.Control.Infrastructure.MQTT;

public class MqttBackgroundService : BackgroundService
{
    private readonly IMqttService _mqttService;
    private readonly ILogger<MqttBackgroundService> _logger;

    public MqttBackgroundService(
        IMqttService mqttService,
        ILogger<MqttBackgroundService> logger)
    {
        _mqttService = mqttService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MQTT Background Service starting...");

        await _mqttService.ConnectAsync(stoppingToken);

        // Reconnection is handled by MqttService via DisconnectedAsync → TryReconnect.
        // This task just keeps the service alive until the host shuts down.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _logger.LogInformation("MQTT Background Service stopping...");
        await _mqttService.DisconnectAsync();
    }
}