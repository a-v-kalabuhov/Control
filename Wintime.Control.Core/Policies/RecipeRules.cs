using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Строка рецепта на входе проверки (BL-36). Тип — из <c>Material.Type</c>,
/// признак архива — из <c>Material.IsActive</c>.
/// </summary>
public sealed record RecipeRow(Guid MaterialId, MaterialType MaterialType, bool MaterialIsActive, decimal Percent);

/// <summary>
/// Правила рецепта изделия и его копии в задании (BL-36). Чистые функции, без БД.
/// Сумма пластиков ≠ 100 % — не ошибка: это предупреждение в UI (см. <see cref="IsSumOk"/>).
/// </summary>
public static class RecipeRules
{
    public const int MaxIngredients = 8;

    /// <summary>
    /// Проверить набор строк рецепта. <paramref name="existingMaterialIds"/> — материалы,
    /// которые уже были в рецепте: архивную строку с таким материалом сохранять можно,
    /// добавлять архивный материал — нет.
    /// </summary>
    public static void Validate(IReadOnlyList<RecipeRow> rows, ISet<Guid> existingMaterialIds)
    {
        if (rows.Count > MaxIngredients)
            throw new DomainException($"В рецепте не может быть больше {MaxIngredients} ингредиентов.");

        var seen = new HashSet<Guid>();
        var plasticSum = 0m;

        foreach (var row in rows)
        {
            if (row.Percent <= 0m || row.Percent > 100m || decimal.Round(row.Percent, 2) != row.Percent)
                throw new DomainException("Процент ингредиента — от 0,01 до 100, не больше двух знаков после запятой.");
            if (!seen.Add(row.MaterialId))
                throw new DomainException("Материал указан в рецепте дважды.");
            if (!row.MaterialIsActive && !existingMaterialIds.Contains(row.MaterialId))
                throw new DomainException("Архивный материал нельзя добавить в рецепт.");
            if (row.MaterialType == MaterialType.Plastic)
                plasticSum += row.Percent;
        }

        if (rows.Count > 0 && plasticSum == 0m)
            throw new DomainException("В рецепте должен быть хотя бы один пластик.");
    }

    /// <summary>Сумма процентов пластиков; краситель не учитывается.</summary>
    public static decimal PlasticSum(IEnumerable<RecipeRow> rows) =>
        rows.Where(r => r.MaterialType == MaterialType.Plastic).Sum(r => r.Percent);

    /// <summary>Пустой рецепт допустим; иначе сумма пластиков должна быть ровно 100 %.</summary>
    public static bool IsSumOk(IReadOnlyList<RecipeRow> rows) =>
        rows.Count == 0 || PlasticSum(rows) == 100m;
}
