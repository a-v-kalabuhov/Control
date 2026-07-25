namespace Wintime.Control.Core.Entities;

/// <summary>
/// Тип изделия / номенклатурная позиция. Каталог-фундамент под заказы (PZP-05)
/// и партии/паспорт (BL-27). См. ADR-0007.
/// </summary>
public class ProductType : BaseEntity
{
    public string Article { get; set; } = string.Empty; // уникальный человекочитаемый артикул (CRM/1С)
    public string Name { get; set; } = string.Empty;    // наименование изделия
    public bool IsActive { get; set; } = true;          // архивный флаг (мягкое удаление)

    // Navigation
    public ICollection<Mold> Molds { get; set; } = new List<Mold>();
}
