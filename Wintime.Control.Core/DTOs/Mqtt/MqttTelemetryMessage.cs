namespace Wintime.Control.Core.DTOs.Mqtt;

public class MqttTelemetryMessage
{
    public long Ts { get; set; } // Unix timestamp
    public string DeviceId { get; set; } = string.Empty;
    public string? TemplateVersion { get; set; }
    public MqttTelemetryData Data { get; set; } = new();
}

public class MqttTelemetryData
{
    public string? Status { get; set; } // Auto, Manual, Alarm, Offline
    public int? Cycles { get; set; }
    public decimal? CycleTime { get; set; }
    public decimal? TempZone1 { get; set; }
    public decimal? TempZone2 { get; set; }
    public decimal? TempZone3 { get; set; }
    public decimal? TempZone4 { get; set; }
    public decimal? PressureInject { get; set; }
    public decimal? ScrewPosition { get; set; }
    public decimal? Cushion { get; set; }
    // Динамические параметры через Dictionary
    public Dictionary<string, object>? Additional { get; set; }
}