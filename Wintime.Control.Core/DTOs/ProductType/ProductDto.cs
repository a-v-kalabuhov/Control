namespace Wintime.Control.Core.DTOs.ProductType;

public class ProductDto
{
    public Guid Id { get; set; }
    public string Article { get; set; } = string.Empty;      // часть артикула
    public string FullArticle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PartWeightGrams { get; set; }
    public int? Cavities { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
    public bool HasSample { get; set; }
    public bool IsActive { get; set; }
    public bool IsUsed { get; set; }                         // есть в заказах/заданиях — удалить нельзя
    public List<RecipeIngredientDto> Recipe { get; set; } = new();   // рецепт изделия (BL-36)
}
