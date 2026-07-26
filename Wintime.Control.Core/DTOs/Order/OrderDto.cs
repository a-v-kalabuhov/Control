using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Order;

public class OrderDto
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime DueDate { get; set; }
    public Guid ProductTypeId { get; set; }
    public string? ProductTypeArticle { get; set; }
    public string? ProductTypeName { get; set; }
    public int Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
    // Агрегаты прогресса (derive-on-read)
    public int ProducedQuantity { get; set; }   // Σ ActualQuantity
    public int DefectQuantity { get; set; }      // Σ DefectQuantity
    public int GoodQuantity { get; set; }        // Σ(Actual − Defect)
    public decimal ProgressPercent { get; set; } // GoodQuantity / Quantity * 100 (не капается)
    public int TaskCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
