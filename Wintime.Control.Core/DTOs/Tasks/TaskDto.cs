namespace Wintime.Control.Core.DTOs.Tasks;

public class TaskDto
{
    public Guid Id { get; set; }
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public Guid MoldId { get; set; }
    public string? MoldName { get; set; }
    public string? PersonnelId { get; set; }
    public string? PersonnelName { get; set; }
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public decimal ActualMaterialWeightGrams { get; set; }
    public decimal ProgressPercent { get; set; }
    public Enums.TaskStatus Status { get; set; }
    public DateOnly PlannedDate { get; set; }
    public TimeOnly? PlannedSetupStartTime { get; set; }

    /// <summary>Эффективное плановое начало, UTC (PlannedTaskStart). Вычисляется, не хранится.</summary>
    public DateTime PlannedStartAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? SetupStartedAt { get; set; }
    public DateTime? MoldVerifiedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public string? Note { get; set; }
    // PZP-05
    public Guid? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    /// <summary>Дата коммерческого заказа (необязательна у заказа).</summary>
    public DateTime? OrderDate { get; set; }
    public int DefectQuantity { get; set; }
    // Рабочий режим и эталоны цикла (спека 2026-08-01)
    public Enums.WorkMode WorkMode { get; set; }
    public int? PlannedFullCycleSeconds { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
    // PZP-14: норматив наладки и буфер выпуска (снапшот) + расчётный план (не хранится)
    public int PlannedSetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
    /// <summary>Плановая длительность выпуска, с (TaskPlanCalculator); null — нет эталона/гнёзд.</summary>
    public long? PlannedReleaseSeconds { get; set; }
    /// <summary>Буфер выпуска, с; null — нет эталона/гнёзд.</summary>
    public long? PlannedReleaseBufferSeconds { get; set; }
    /// <summary>Плановое окончание (начало + наладка + выпуск + буфер), UTC; null — нет эталона/гнёзд.</summary>
    public DateTime? PlannedEndAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}