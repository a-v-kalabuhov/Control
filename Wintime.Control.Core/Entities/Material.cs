using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Материал — пластик или краситель (PZP-22). Ингредиент рецепта изделия (BL-36).
/// Уникальность внутри типа без учёта регистра: (тип, артикул) и (тип, производитель, наименование).
/// </summary>
public class Material : BaseEntity
{
    public MaterialType Type { get; set; }               // задаётся при создании, не меняется
    public string Article { get; set; } = string.Empty;  // заглавными (ProductArticle.Normalize)
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }               // обязателен у красителя
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; } = true;           // архивный флаг (мягкое удаление)
}
