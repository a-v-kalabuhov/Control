namespace Wintime.Control.Core.DTOs.Imm;

public class ImmStatusDto
{
    public Guid ImmId { get; set; }
    public string Status { get; set; } = string.Empty; // Auto, Manual, Alarm, Offline
    public Guid? CurrentTaskId { get; set; }
    public Guid? CurrentMoldId { get; set; }
    public decimal CurrentCycleTime { get; set; }
    public DateTime LastUpdate { get; set; }
}