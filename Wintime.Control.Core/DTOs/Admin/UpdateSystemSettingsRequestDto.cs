namespace Wintime.Control.Core.DTOs.Admin;

public class UpdateSystemSettingsRequestDto
{
    public string? MqttBrokerUrl { get; set; }
    public int? MqttPort { get; set; }
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }
    public int? SessionTimeoutMinutes { get; set; }
    public int? TelemetryIntervalSeconds { get; set; }
}