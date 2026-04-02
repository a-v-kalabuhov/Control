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
        _logger.LogInformation("🚀 MQTT Background Service starting...");

        await _mqttService.ConnectAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            if (!_mqttService.IsConnected)
            {
                _logger.LogWarning("⚠️ MQTT is not connected. Attempting to reconnect...");
                await _mqttService.ConnectAsync(stoppingToken);
            }
        }

        _logger.LogInformation("🛑 MQTT Background Service stopping...");
        await _mqttService.DisconnectAsync();
    }
}