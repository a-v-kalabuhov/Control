using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>
/// Строка копии рецепта в задании (BL-36). <c>OriginalMaterial*</c> — материал рецепта изделия
/// на момент копирования: расхождение с текущим материалом и означает «рецепт правился вручную».
/// </summary>
public class TaskRecipeIngredientDto
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public MaterialType MaterialType { get; set; }
    public string MaterialArticle { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public Guid OriginalMaterialId { get; set; }
    public string OriginalMaterialArticle { get; set; } = string.Empty;
    public string OriginalMaterialName { get; set; } = string.Empty;
    public decimal Percent { get; set; }
}
