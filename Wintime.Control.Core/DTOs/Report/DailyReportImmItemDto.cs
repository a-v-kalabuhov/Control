namespace Wintime.Control.Core.DTOs.Report;

public class DailyReportImmItemDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public string? MoldName { get; set; }
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int CycleCount { get; set; }
    public int WorkTimeSeconds { get; set; }
    public int DowntimeSeconds { get; set; }
    public int OfflineSeconds { get; set; }
    public decimal AvgCycleTime { get; set; }
    public decimal Efficiency { get; set; }
    public decimal RawMaterialKg { get; set; }
    public List<DowntimeDetailDto> DowntimeDetails { get; set; } = new();
}