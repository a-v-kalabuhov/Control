namespace Wintime.Control.Emulator.Models;

using System.Text.Json.Serialization;

public class SignalValueDto
{
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
    [JsonPropertyName("error")]
    public bool Error { get; set; }
}

public class CurrentCycleDto
{
    [JsonPropertyName("number")]
    public int Number { get; set; }
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }
    [JsonPropertyName("injectionStartTime")]
    public DateTime? InjectionStartTime { get; set; }
    [JsonPropertyName("cushion")]
    public decimal? Cushion { get; set; }
}

public class CompletedCycleDto
{
    [JsonPropertyName("number")]
    public int Number { get; set; }
    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }
    [JsonPropertyName("endTime")]
    public DateTime EndTime { get; set; }
    [JsonPropertyName("injectionStartTime")]
    public DateTime? InjectionStartTime { get; set; }
    [JsonPropertyName("cushion")]
    public decimal? Cushion { get; set; }
}

/// <summary>
/// Данные для отправки в MQTT брокер.
/// Сообщение содердит время отправки сообщения и показания датчиков.
/// </summary>
public class TelemetryPayload
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("sensors")]
    public Dictionary<string, SignalValueDto> Sensors { get; set; } = new();

    [JsonPropertyName("currentCycle")]
    public CurrentCycleDto? CurrentCycle { get; set; }

    [JsonPropertyName("lastCycle")]
    public CompletedCycleDto? LastCycle { get; set; }
}
