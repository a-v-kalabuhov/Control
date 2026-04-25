namespace Wintime.Control.Core.DTOs.Mqtt;

public class MqttTelemetryMessage
{
    /// <summary>
    /// Unix timestamp
    /// </summary>
    public long Timestamp { get; set; }
    /// <summary>
    /// ID устройства
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    /// <summary>
    /// Список показаний датчиков
    /// </summary>
    public Dictionary<string, object> Sensors = [];
}