using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

public class CreateTaskRequestDto
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public Guid? OrderId { get; set; }
    public string? PersonnelId { get; set; }
    public int PlanQuantity { get; set; }
    public string? Note { get; set; }
    public DateTime? PlannedDate { get; set; }

    /// <summary>Рабочий режим; не передан — «автомат».</summary>
    public WorkMode WorkMode { get; set; } = WorkMode.Auto;

    /// <summary>Эталон полного цикла, секунды. Обязателен.</summary>
    public int? PlannedFullCycleSeconds { get; set; }

    /// <summary>Эталон цикла литья, секунды. Необязателен.</summary>
    public int? PlannedInjectionCycleSeconds { get; set; }
}
