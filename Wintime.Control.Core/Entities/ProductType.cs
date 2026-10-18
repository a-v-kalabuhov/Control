namespace Wintime.Control.Core.Entities;

/// <summary>
/// Тип изделия. Содержит изделия (<c>Product</c>), ПФ ссылается на тип. См. ADR-0020.
/// </summary>
public class ProductType : BaseEntity
{
    public string Article { get; set; } = string.Empty; // уникальный человекочитаемый артикул (CRM/1С)
    public string Name { get; set; } = string.Empty;    // наименование изделия
    public bool IsActive { get; set; } = true;          // архивный флаг (мягкое удаление)

    // Navigation
    public ICollection<Mold> Molds { get; set; } = new List<Mold>();
    public ICollection<Product> Products { get; set; } = new List<Product>(); // всегда ≥ 1 (PZP-19)
}
