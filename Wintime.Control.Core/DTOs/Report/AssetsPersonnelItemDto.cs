namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelItemDto
{
    public string PersonnelId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    /// <summary>Задания, завершённые (Completed) в периоде.</summary>
    public int CompletedTasks { get; set; }
    /// <summary>Наладки, завершённые в периоде (StartedAt ∈ период).</summary>
    public int CompletedSetups { get; set; }
    /// <summary>Среднее время наладки, секунды.</summary>
    public decimal AvgSetupTime { get; set; }
    /// <summary>Суммарное время наладок, завершённых в периоде, секунды.</summary>
    public int TotalSetupSeconds { get; set; }
}
