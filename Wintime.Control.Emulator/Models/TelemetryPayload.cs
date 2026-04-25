namespace Wintime.Control.Emulator.Models;

using System.Text.Json.Serialization;

public class TelemetryPayload
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sensors")]
    public Dictionary<string, object> Sensors { get; set; } = new();
}