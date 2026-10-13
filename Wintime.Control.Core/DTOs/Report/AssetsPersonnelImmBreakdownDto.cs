namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelImmBreakdownDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public int CompletedTasks { get; set; }
    public int CompletedSetups { get; set; }
    public decimal AvgSetupTime { get; set; }
    public int TotalSetupSeconds { get; set; }
}
