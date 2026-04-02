namespace Wintime.Control.Infrastructure.MQTT;

public interface IMqttService
{
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    bool IsConnected { get; }
}