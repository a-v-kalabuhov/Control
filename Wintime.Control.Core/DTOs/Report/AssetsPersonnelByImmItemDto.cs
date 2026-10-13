namespace Wintime.Control.Core.DTOs.Report;

public class AssetsPersonnelByImmItemDto
{
    public string PersonnelId { get; set; } = string.Empty;
    public string? FullName { get; set; }
    /// <summary>Задания, завершённые (Complete) в периоде — по CompletedAt, в т.ч. закрытые после завершения.</summary>
    public int CompletedTasks { get; set; }
    /// <summary>Наладки, завершённые в периоде (StartedAt ∈ период).</summary>
    public int CompletedSetups { get; set; }
    /// <summary>Среднее время наладки, секунды.</summary>
    public decimal AvgSetupTime { get; set; }
    /// <summary>Суммарное время наладок, завершённых в периоде, секунды.</summary>
    public int TotalSetupSeconds { get; set; }
    public List<AssetsPersonnelImmBreakdownDto> ImmBreakdown { get; set; } = [];
}
