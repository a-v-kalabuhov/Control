namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelItemDto
{
    public string PersonnelId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public int CompletedTasks { get; set; }
    public int TotalWorkSeconds { get; set; }
    public decimal AvgSetupTime { get; set; }
}