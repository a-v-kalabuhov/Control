using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class UpdateTaskRequestDto
{
    public int? PlanQuantity { get; set; }
    public string? Note { get; set; }
    public DateTime? PlannedDate { get; set; }
    public Enums.TaskStatus? Status { get; set; }

    /// <summary>Рабочий режим; <c>null</c> — не менять.</summary>
    public WorkMode? WorkMode { get; set; }

    /// <summary>Эталон полного цикла, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды; <c>null</c> — не менять.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }
}
