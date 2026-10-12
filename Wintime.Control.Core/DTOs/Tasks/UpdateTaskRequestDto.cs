using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class UpdateTaskRequestDto
{
    public int? PlanQuantity { get; set; }
    public string? Note { get; set; }
    /// <summary>Плановая дата; <c>null</c> — не менять.</summary>
    public DateOnly? PlannedDate { get; set; }

    /// <summary>Плановое время начала наладки; <c>null</c> — не менять.</summary>
    public TimeOnly? PlannedSetupStartTime { get; set; }

    /// <summary>Явно очистить время начала наладки (в PUT <c>null</c> означает «не менять»).</summary>
    public bool ClearPlannedSetupStartTime { get; set; }

    /// <summary>Рабочий режим; <c>null</c> — не менять.</summary>
    public WorkMode? WorkMode { get; set; }

    /// <summary>Эталон полного цикла, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }

    /// <summary>Норматив наладки, минуты; <c>null</c> — не менять.</summary>
    public int? PlannedSetupMinutes { get; set; }

    /// <summary>Буфер выпуска: фикс. часть, минуты; <c>null</c> — не менять.</summary>
    public int? ReleaseBufferFixedMinutes { get; set; }

    /// <summary>Буфер выпуска: процент; <c>null</c> — не менять.</summary>
    public int? ReleaseBufferPercent { get; set; }
}
