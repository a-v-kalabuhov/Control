namespace Wintime.Control.Emulator.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Данные для отправки в MQTT брокер.
/// Сообщение содердит время отправки сообщения и показания датчиков.
/// </summary>
public class TelemetryPayload
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sensors")]
    public Dictionary<string, object> Sensors { get; set; } = new();
}