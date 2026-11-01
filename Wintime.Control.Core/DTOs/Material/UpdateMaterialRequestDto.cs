namespace Wintime.Control.Core.DTOs.Material;

/// <summary>
/// Частичная правка: <c>null</c> — поле не меняется, пустая строка у необязательного поля — очистить.
/// Тип материала не меняется (PZP-22), поэтому его здесь нет.
/// </summary>
public class UpdateMaterialRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
    public bool? IsActive { get; set; }
}
