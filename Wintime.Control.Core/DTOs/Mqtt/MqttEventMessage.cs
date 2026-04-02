namespace Wintime.Control.Core.DTOs.Mqtt;

public class MqttEventMessage
{
    public long Ts { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty; // alarm_start, cycle_complete, downtime_start, etc.
    public MqttEventPayload Payload { get; set; } = new();
}

public class MqttEventPayload
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public int? CycleId { get; set; }
    public decimal? Duration { get; set; }
    public string? Result { get; set; } // success, aborted
}