namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public List<EquipmentReportImmItemDto> ImmData { get; set; } = new();
}