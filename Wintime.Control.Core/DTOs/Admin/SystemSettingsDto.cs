namespace Wintime.Control.Core.DTOs.Admin;

public class SystemSettingsDto
{
    public string MqttBrokerUrl { get; set; } = string.Empty;
    public int MqttPort { get; set; }
    public string? MqttUsername { get; set; }
    public string? DatabaseConnectionString { get; set; }
    public int SessionTimeoutMinutes { get; set; }
    public int TelemetryIntervalSeconds { get; set; }
}