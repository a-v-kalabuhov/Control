namespace Wintime.Control.Core.DTOs.Admin;

public class MqttTestRequestDto
{
    public string BrokerUrl { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}