namespace Wintime.Control.Core.DTOs.Dashboard;

public class ShiftUtilizationDto
{
    /// <summary>Средняя загрузка за период, % (0..100)</summary>
    public decimal Utilization { get; set; }

    /// <summary>Количество активных станков в расчёте</summary>
    public int MachineCount { get; set; }

    /// <summary>Начало периода (UTC)</summary>
    public DateTime From { get; set; }

    /// <summary>Конец периода (UTC)</summary>
    public DateTime To { get; set; }
}
