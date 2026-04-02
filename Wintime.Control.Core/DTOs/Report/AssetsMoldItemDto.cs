namespace Wintime.Control.Core.DTOs.Report;

public class AssetsMoldItemDto
{
    public Guid MoldId { get; set; }
    public string? MoldName { get; set; }
    public int TotalCycles { get; set; }
    public decimal WorkHours { get; set; }
    public int RemainingResource { get; set; }
}