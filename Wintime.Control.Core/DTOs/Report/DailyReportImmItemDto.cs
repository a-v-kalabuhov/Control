namespace Wintime.Control.Core.DTOs.Report;

public class DailyReportImmItemDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public string? MoldName { get; set; }
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int CycleCount { get; set; }
    /// <summary>Секунды по эффективным состояниям за период до «сейчас» (7 ключей, включая NoData) — как в «Производительности».</summary>
    public Dictionary<string, int> Seconds { get; set; } = new();
    /// <summary>Работа / (всё − Нет связи − Нет данных) × 100; null — известного времени нет.</summary>
    public decimal? Efficiency { get; set; }
    public decimal RawMaterialKg { get; set; }
    public List<DowntimeDetailDto> DowntimeDetails { get; set; } = new();
    public List<TimelineItemDto> Timeline { get; set; } = new();
}