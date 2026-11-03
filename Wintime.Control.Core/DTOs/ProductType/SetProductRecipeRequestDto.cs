namespace Wintime.Control.Core.DTOs.ProductType;

/// <summary>Тело <c>PUT /api/producttypes/{id}/products/{productId}/recipe</c> (BL-36).</summary>
public class SetProductRecipeRequestDto
{
    public List<RecipeIngredientInputDto>? Ingredients { get; set; }
}

public class RecipeIngredientInputDto
{
    public Guid MaterialId { get; set; }
    public decimal Percent { get; set; }
}
