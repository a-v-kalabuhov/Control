namespace Wintime.Control.Core.DTOs.Imm;

public class TaskCycleStatsDto
{
    public Guid TaskId { get; set; }
    public string? TaskNumber { get; set; }
    public Guid MoldId { get; set; }
    public string? MoldName { get; set; }
    public int Cycles { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}