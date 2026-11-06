namespace Wintime.Control.Core.DTOs.ProductType;

public class CreateProductTypeRequestDto
{
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Company { get; set; }
    /// <summary>Тарная единица по умолчанию, штук; null — не задана.</summary>
    public int? PackagingUnit { get; set; }
}
