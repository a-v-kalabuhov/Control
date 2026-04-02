using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Task;

public class UpdateTaskRequestDto
{
    public int? PlanQuantity { get; set; }
    public string? Note { get; set; }
    public Enums.TaskStatus? Status { get; set; }
}