namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelByImmItemDto
{
    public string PersonnelId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public int CompletedTasks { get; set; }
    public int TotalWorkSeconds { get; set; }
    public decimal AvgSetupTime { get; set; }
    public int TotalSetupSeconds { get; set; }
    public int WorkedShifts { get; set; }
    public List<AssetsPersonnelImmBreakdownDto> ImmBreakdown { get; set; } = [];
}
