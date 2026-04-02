namespace Wintime.Control.Core.DTOs.Task;

public class CreateTaskRequestDto
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public string? PersonnelId { get; set; }
    public int PlanQuantity { get; set; }
    public string? Note { get; set; }
}