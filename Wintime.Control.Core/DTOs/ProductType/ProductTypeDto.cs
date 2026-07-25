namespace Wintime.Control.Core.DTOs.ProductType;

public class ProductTypeDto
{
    public Guid Id { get; set; }
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
