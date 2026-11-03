namespace Wintime.Control.Core.Entities;

/// <summary>
/// Копия строки рецепта в задании на момент создания (или смены изделия), BL-36.
/// <see cref="MaterialId"/> — текущий материал, <see cref="OriginalMaterialId"/> — материал
/// рецепта изделия: их расхождение и есть «рецепт правился вручную». Процент не меняется —
/// разрешена только замена материала (<see cref="ShiftTask.ReplaceIngredient"/>).
/// </summary>
public class ShiftTaskRecipeIngredient : BaseEntity
{
    public Guid ShiftTaskId { get; set; }
    public ShiftTask? ShiftTask { get; set; }
    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }
    public Guid OriginalMaterialId { get; set; }
    public Material? OriginalMaterial { get; set; }
    public decimal Percent { get; set; }
    public int SortOrder { get; set; }
}
