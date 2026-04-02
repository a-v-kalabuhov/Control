namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportImmItemDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public int TotalWorkSeconds { get; set; }
    public int TotalDowntimeSeconds { get; set; }
    public int TotalCycles { get; set; }
    public decimal AvgEfficiency { get; set; }
}