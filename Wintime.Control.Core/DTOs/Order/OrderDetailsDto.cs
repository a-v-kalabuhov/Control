namespace Wintime.Control.Core.DTOs.Order;

public class OrderDetailsDto : OrderDto
{
    public List<OrderTaskSummaryDto> Tasks { get; set; } = new();
}
