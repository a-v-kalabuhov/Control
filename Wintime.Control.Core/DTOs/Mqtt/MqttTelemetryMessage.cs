using System.Text.Json.Serialization;

namespace Wintime.Control.Core.DTOs.Mqtt;

public class MqttTelemetryMessage
{
    /// <summary>
    /// Момент формирования сообщения продюсером. Всегда UTC (<see cref="DateTimeKind.Utc"/>):
    /// значение уходит в колонку <c>timestamptz</c>, а <c>Kind=Unspecified</c> там даёт
    /// исключение Npgsql. Точность — как пришла от продюсера, собственного округления нет.
    /// Единственный производитель значения — <c>DecodeTelemetryDataHandler</c>.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
    public Dictionary<string, SignalValue> Sensors { get; set; } = [];
    public CycleSnapshot? CurrentCycle { get; set; }
    public CompletedCycleSnapshot? LastCycle { get; set; }
}