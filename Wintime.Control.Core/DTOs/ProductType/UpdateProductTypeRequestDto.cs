namespace Wintime.Control.Core.DTOs.ProductType;

public class UpdateProductTypeRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Company { get; set; }                // null — не менять; пустая строка — очистить
    public int? PackagingUnit { get; set; }             // null — не менять
    public bool ClearPackagingUnit { get; set; }        // явно очистить тарную единицу
    public bool? IsActive { get; set; }
}
