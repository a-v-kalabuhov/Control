namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>
/// Итоги выполнения задания по окну выпуска <c>[StartedAt, CompletedAt ?? ClosedAt ?? now)</c>.
/// Не хранятся — считаются на лету (<c>ITaskResultsService</c>). Проценты и отклонения считает фронт.
/// </summary>
public class TaskResultsDto
{
    /// <summary>false — выпуск не начинался; остальные блоки null.</summary>
    public bool ReleaseStarted { get; set; }
    public DateTime? ReleaseFrom { get; set; }
    public DateTime? ReleaseTo { get; set; }
    /// <summary>Окно открыто (задание не завершено и не закрыто) — правый край = «сейчас».</summary>
    public bool InProgress { get; set; }
    public TaskOutputResultsDto? Output { get; set; }
    public TaskCycleResultsDto? Cycles { get; set; }
    public TaskTimeResultsDto? Time { get; set; }
}

public class TaskOutputResultsDto
{
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int DefectQuantity { get; set; }
    /// <summary>Выпущено − брак.</summary>
    public int GoodQuantity { get; set; }
    /// <summary>max(0, выпущено − план).</summary>
    public int OverrunQuantity { get; set; }
    /// <summary>«Причина отклонения от плана» — CloseReason завершённого задания.</summary>
    public string? DeviationReason { get; set; }
}

public class TaskCycleResultsDto
{
    /// <summary>Закрытые успешные циклы задания.</summary>
    public int Count { get; set; }
    /// <summary>AVG(InjectionDurationMs)/1000; null — циклов меньше 3 или литьё не измерялось.</summary>
    public double? AvgInjectionSeconds { get; set; }
    public int? PlannedInjectionSeconds { get; set; }
    /// <summary>AVG(DurationSeconds) + AVG(PauseDurationMs)/1000 (ADR-0017); null — циклов меньше 3.</summary>
    public double? AvgFullCycleSeconds { get; set; }
    public int? PlannedFullCycleSeconds { get; set; }
    /// <summary>Подушка по закрытым циклам (сырые единицы коннектора); null — циклов меньше 3 или подушка не измерялась.</summary>
    public decimal? AvgCushion { get; set; }
    public decimal? MinCushion { get; set; }
    public decimal? MaxCushion { get; set; }
}

public class TaskTimeResultsDto
{
    public int WindowSeconds { get; set; }
    public int ProductionSeconds { get; set; }
    public int DowntimeSeconds { get; set; }
    /// <summary>События простоя на ТПА задания, пересекающие окно (авто и ручные).</summary>
    public int DowntimeCount { get; set; }
    public int OfflineSeconds { get; set; }
}
