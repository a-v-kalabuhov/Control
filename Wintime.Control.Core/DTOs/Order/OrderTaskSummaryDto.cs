using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Order;

public class OrderTaskSummaryDto
{
    public Guid TaskId { get; set; }
    public string? ImmName { get; set; }
    public string? MoldName { get; set; }
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public int DefectQuantity { get; set; }
    public Wintime.Control.Core.Enums.TaskStatus Status { get; set; }
}
