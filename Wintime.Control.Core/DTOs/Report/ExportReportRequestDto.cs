namespace Wintime.Control.Core.DTOs.Report;

public class ExportReportRequestDto
{
    public string ReportType { get; set; } = string.Empty; // Daily, Equipment, Assets
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public List<Guid>? ImmIds { get; set; }
    public Guid? ShiftId { get; set; }
}