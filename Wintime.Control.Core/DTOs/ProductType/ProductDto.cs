namespace Wintime.Control.Core.DTOs.ProductType;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Article { get; set; } = string.Empty;      // часть артикула
    public string FullArticle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsUsed { get; set; }                         // есть в заказах/заданиях — удалить нельзя
}
