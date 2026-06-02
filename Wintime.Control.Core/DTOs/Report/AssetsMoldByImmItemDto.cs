namespace Wintime.Control.Core.DTOs.Report;

public class AssetsMoldByImmItemDto
{
    public Guid MoldId { get; set; }
    public string? MoldName { get; set; }
    public int TotalCycles { get; set; }
    public decimal WorkHours { get; set; }
    public int MaxResourceCycles { get; set; }
    public int? To1Cycles { get; set; }
    public int? To2Cycles { get; set; }
    public int AllTimeTotalCycles { get; set; }
    public int RemainingResource { get; set; }
    public List<AssetsMoldImmBreakdownDto> ImmBreakdown { get; set; } = [];
}
