namespace Wintime.Control.Core.DTOs.Report;

public class AssetsMoldImmBreakdownDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public int TotalCycles { get; set; }
    public decimal WorkHours { get; set; }
}
