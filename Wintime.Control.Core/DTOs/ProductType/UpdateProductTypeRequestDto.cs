namespace Wintime.Control.Core.DTOs.ProductType;

public class UpdateProductTypeRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Company { get; set; }                // null — не менять; пустая строка — очистить
    public bool? IsActive { get; set; }
}
