using Wintime.Control.Core.Policies;

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
    public decimal PartWeightGrams { get; set; }            // вес одной детали, граммы (PZP-24)
    public int? PlannedInjectionCycleSeconds { get; set; } // эталон цикла литья, секунды; необязателен
    public bool HasSample { get; set; }                   // есть физический образец изделия (PZP-29)
    public bool IsActive { get; set; } = true;              // архивный флаг
    public List<ProductRecipeIngredient> RecipeIngredients { get; set; } = new();   // рецепт (BL-36)

    /// <summary>
    /// BL-36: заменить рецепт целиком. Порядок строк — порядок <paramref name="rows"/>.
    /// Строки согласуются по материалу (обновляем существующие, а не пересоздаём) —
    /// иначе уникальный индекс <c>(ProductId, MaterialId)</c> ловит конфликт на сохранении.
    /// <paramref name="existingMaterialIds"/> — материалы, уже бывшие в рецепте (архивную
    /// строку сохранить можно, добавить архивный материал — нельзя).
    /// </summary>
    public void SetRecipe(IReadOnlyList<RecipeRow> rows, ISet<Guid> existingMaterialIds)
    {
        RecipeRules.Validate(rows, existingMaterialIds);

        var keep = rows.Select(r => r.MaterialId).ToHashSet();
        RecipeIngredients.RemoveAll(i => !keep.Contains(i.MaterialId));

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var existing = RecipeIngredients.FirstOrDefault(i => i.MaterialId == row.MaterialId);
            if (existing == null)
            {
                RecipeIngredients.Add(new ProductRecipeIngredient
                {
                    MaterialId = row.MaterialId, Percent = row.Percent, SortOrder = index
                });
            }
            else
            {
                existing.Percent = row.Percent;
                existing.SortOrder = index;
            }
        }
    }
}
