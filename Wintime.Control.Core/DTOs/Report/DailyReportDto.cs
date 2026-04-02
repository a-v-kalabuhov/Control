namespace Wintime.Control.Core.DTOs.Report;

public class DailyReportDto
{
    public DateTime Date { get; set; }
    public List<DailyReportImmItemDto> ImmData { get; set; } = new();
}