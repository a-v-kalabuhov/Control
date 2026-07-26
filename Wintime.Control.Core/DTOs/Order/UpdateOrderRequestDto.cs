namespace Wintime.Control.Core.DTOs.Order;

public class UpdateOrderRequestDto
{
    public string? Number { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? DueDate { get; set; }
    public Guid? ProductTypeId { get; set; }
    public int? Quantity { get; set; }
    public string? Note { get; set; }
}
