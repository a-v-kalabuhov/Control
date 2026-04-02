namespace Wintime.Control.Core.DTOs.Report;

public class AssetsReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public List<AssetsMoldItemDto>? MoldData { get; set; }
    public List<AssetsPersonnelItemDto>? PersonnelData { get; set; }
}