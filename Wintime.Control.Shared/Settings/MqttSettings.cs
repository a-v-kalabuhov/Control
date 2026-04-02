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

public class SensorThreshold
{
    public string ParameterName { get; set; } = string.Empty;
    public string ParameterType { get; set; } = "numeric"; // numeric, discrete, string
    public decimal Threshold { get; set; } = 0;
    public int TimeoutSeconds { get; set; } = 300;
    public List<string>? AllowedValues { get; set; } // Для дискретных значений
}

public class TemplateConfig
{
    public string TemplateName { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = string.Empty;
    public List<SensorThreshold> Sensors { get; set; } = new();
}