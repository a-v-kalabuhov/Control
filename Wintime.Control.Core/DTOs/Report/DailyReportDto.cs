namespace Wintime.Control.Core.DTOs.Report;

public class DailyReportDto
{
    public DateTime Date { get; set; }
    public int? ShiftNumber { get; set; }
    public string? ShiftStartTime { get; set; }
    public string? ShiftEndTime { get; set; }
    /// <summary>Начало периода отчёта (смена или сутки завода), UTC. Без полей графика ±30 мин.</summary>
    public DateTime PeriodStart { get; set; }
    /// <summary>Конец периода отчёта (смена или сутки завода), UTC, не включая.</summary>
    public DateTime PeriodEnd { get; set; }
    public List<DailyReportImmItemDto> ImmData { get; set; } = new();
}