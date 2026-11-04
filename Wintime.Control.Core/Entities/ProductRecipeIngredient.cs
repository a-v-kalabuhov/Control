namespace Wintime.Control.Core.Entities;

/// <summary>
/// Строка рецепта изделия (BL-36): материал и его доля в процентах от массы изделия.
/// Тип ингредиента здесь не хранится — это <see cref="Material.Type"/>. Строки заменяются
/// набором целиком через <see cref="Product.SetRecipe"/>, порядок — <see cref="SortOrder"/>.
/// </summary>
public class ProductRecipeIngredient : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Percent { get; set; }
    public int SortOrder { get; set; }
}
