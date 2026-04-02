namespace Wintime.Control.Core.DTOs.Imm;

public class ImmStatisticsDto
{
    public Guid ImmId { get; set; }
    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }
    public int TotalCycles { get; set; }
    public List<TaskCycleStatsDto> CyclesByTask { get; set; } = new();
    public int CyclesInSetup { get; set; }
    public int CyclesInAlarm { get; set; }
    public decimal AvgCycleTime { get; set; }
}