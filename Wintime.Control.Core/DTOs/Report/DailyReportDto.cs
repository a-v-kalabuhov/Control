namespace Wintime.Control.Core.DTOs.Report;

public class DailyReportDto
{
    public DateTime Date { get; set; }
    public int? ShiftNumber { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftEndTime { get; set; }
    public List<DailyReportImmItemDto> ImmData { get; set; } = new();
}