namespace Wintime.Control.Core.DTOs.Task;

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
    public decimal ProgressPercent { get; set; }
    public Enums.TaskStatus Status { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? CloseReason { get; set; }
    public string? Note { get; set; }
}