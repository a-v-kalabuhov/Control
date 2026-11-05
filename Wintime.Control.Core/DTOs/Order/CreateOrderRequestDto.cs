namespace Wintime.Control.Core.DTOs.Order;

public class CreateOrderRequestDto
{
    public string Number { get; set; } = string.Empty;
    public string? Customer { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime DueDate { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}
