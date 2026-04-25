namespace Wintime.Control.Shared.Settings;

public class MqttSettings
{
    public const string SectionName = "MqttSettings";
    
    public string BrokerUrl { get; set; } = string.Empty;
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = "ControlServer";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int ReconnectDelaySeconds { get; set; } = 5;
    public MqttTopics Topics { get; set; } = new();
}

public class MqttTopics
{
    public string Telemetry { get; set; } = "control/imm/+/telemetry";
    public string Events { get; set; } = "control/imm/+/events";
    public string Status { get; set; } = "control/imm/+/status";
}