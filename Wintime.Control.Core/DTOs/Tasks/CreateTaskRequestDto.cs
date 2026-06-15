namespace Wintime.Control.Core.DTOs.Tasks;

public class CreateTaskRequestDto
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public string? PersonnelId { get; set; }
    public int PlanQuantity { get; set; }
    public string? Note { get; set; }
    public DateTime? PlannedDate { get; set; }
}