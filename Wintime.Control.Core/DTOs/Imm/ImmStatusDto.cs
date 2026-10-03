namespace Wintime.Control.Core.DTOs.Imm;

public class ImmStatusDto
{
    public Guid ImmId { get; set; }
    public string Status { get; set; } = string.Empty; // Auto, Manual, Idle, Alarm, Offline
    public string EffectiveStatus { get; set; } = string.Empty;
    public Guid? CurrentTaskId { get; set; }
    public Guid? CurrentMoldId { get; set; }
    /// <summary>Длительность последнего завершённого цикла, сек (из <c>lastCycle</c> MQTT); <c>null</c> — неизвестна.</summary>
    public decimal? CurrentCycleTime { get; set; }
    public DateTime LastUpdate { get; set; }
}