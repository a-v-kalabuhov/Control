using System.Text.Json.Serialization;

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
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
    /// <summary>
    /// Список показаний датчиков
    /// </summary>
    public Dictionary<string, string> Sensors = [];
}