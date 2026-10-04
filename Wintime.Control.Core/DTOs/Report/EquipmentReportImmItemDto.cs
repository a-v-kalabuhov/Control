namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportImmItemDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Секунды по эффективным статусам за период (7 ключей, включая NoData).</summary>
    public Dictionary<string, int> Seconds { get; set; } = new();

    public int TotalCycles { get; set; }
    public decimal AvgCycleSeconds { get; set; }

    /// <summary>Работа / известное время × 100; null — известного времени нет.</summary>
    public decimal? Efficiency { get; set; }

    public List<EquipmentReportDayDto> Days { get; set; } = new();
}
