namespace Wintime.Control.Core.DTOs.Mold;

public class MoldUsageDto
{
    public Guid MoldId { get; set; }
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public Guid? TaskId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int CyclesStart { get; set; }
    public int CyclesEnd { get; set; }
    public int CyclesCount => CyclesEnd - CyclesStart;
}