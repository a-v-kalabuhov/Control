namespace Wintime.Control.Core.DTOs.Imm;

public class ImmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? InventoryNumber { get; set; }
    public string? ConnectorAlias { get; set; }
    public DateTime? CommissioningDate { get; set; }
    public Guid TemplateId { get; set; }
    public string? Manufacturer { get; set; } // Из шаблона (денормализация)
    public string? Model { get; set; } // Из шаблона (денормализация)
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Status { get; set; }
    public string? EffectiveStatus { get; set; } // Production/Setup/Downtime/Unplanned/NoTask/Offline
    public DateTime? LastUpdate { get; set; }
    public Guid? CurrentTaskId { get; set; }
    public string? CurrentMoldName { get; set; }
    public string? CurrentProductArticle { get; set; }
    public string? PersonnelName { get; set; }
    public int? PlanQuantity { get; set; }
    public int? ActualQuantity { get; set; }
    public int CycleCount { get; set; }
    public decimal AvgCycleTime { get; set; }

    /// <summary>
    /// Длительность последнего завершённого цикла, сек — из <c>lastCycle</c> MQTT-сообщения
    /// (кеш <c>IImmCache</c>). <c>null</c> — с момента старта API цикл ещё не приходил.
    /// </summary>
    public decimal? LastCycleTime { get; set; }
    public DateTime? TaskStartedAt { get; set; }

    /// <summary>
    /// Темп выпуска, шт/ч, по среднему полному периоду цикла задания (<c>ReleaseForecast</c>,
    /// ADR-0017). Только для задания в <c>InProgress</c>; иначе <c>null</c>.
    /// </summary>
    public decimal? ReleaseRatePerHour { get; set; }

    /// <summary>Прогноз окончания выпуска (UTC) — без наладки и простоев. <c>null</c> — не посчитать.</summary>
    public DateTime? ReleaseForecastAt { get; set; }
}