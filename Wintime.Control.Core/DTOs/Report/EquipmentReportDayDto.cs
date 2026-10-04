namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportDayDto
{
    /// <summary>Дата локальных суток завода (Kind=Utc, время 00:00).</summary>
    public DateTime Date { get; set; }

    /// <summary>Секунды по эффективным статусам за сутки (7 ключей, сумма = длина суток).</summary>
    public Dictionary<string, int> Seconds { get; set; } = new();
}
