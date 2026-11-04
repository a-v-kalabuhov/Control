namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Тело <c>PUT /api/tasks/{id}/recipe/{ingredientId}</c> (BL-36).</summary>
public class ReplaceRecipeIngredientRequestDto
{
    public Guid MaterialId { get; set; }
}
