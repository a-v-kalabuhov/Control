using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.ProductType;

/// <summary>Строка рецепта изделия (BL-36). Тип — из <c>Material.Type</c>.</summary>
public class RecipeIngredientDto
{
    public Guid MaterialId { get; set; }
    public MaterialType MaterialType { get; set; }
    public string MaterialArticle { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public bool MaterialIsActive { get; set; }
    public decimal Percent { get; set; }
}
