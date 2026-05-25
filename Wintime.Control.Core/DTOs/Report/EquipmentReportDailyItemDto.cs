namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportDailyItemDto
{
    public DateTime Date { get; set; }
    public int TotalWorkSeconds { get; set; }
    public int TotalSetupSeconds { get; set; }
    public int TotalDowntimeSeconds { get; set; }
}
