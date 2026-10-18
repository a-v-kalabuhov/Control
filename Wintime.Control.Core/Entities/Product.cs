namespace Wintime.Control.Core.Entities;

/// <summary>
/// Изделие — товарная позиция внутри типа изделия (PZP-19, ADR-0020), например цвет.
/// Полный артикул = артикул типа + "." + часть артикула изделия; уникален в БД.
/// </summary>
public class Product : BaseEntity
{
    public Guid ProductTypeId { get; set; }
    public ProductType? ProductType { get; set; }
    public string Article { get; set; } = string.Empty;     // часть артикула (заглавными)
    public string Name { get; set; } = string.Empty;        // наименование изделия
    public string FullArticle { get; set; } = string.Empty; // хранимый, пересчитывается при правке артикулов
    public bool IsActive { get; set; } = true;              // архивный флаг
}
