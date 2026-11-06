using System.Data;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Nomenclature;

public sealed class NomenclatureImportResult
{
    public string Status { get; set; } = "ConfirmationRequired";
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int SkippedRows { get; set; }
    public int CreatedTypes { get; set; }
    public int UpdatedTypes { get; set; }
    public int CreatedMolds { get; set; }
    public int UpdatedMolds { get; set; }
    public int CreatedProducts { get; set; }
    public int UpdatedProducts { get; set; }
    public int CreatedMaterials { get; set; }
    public List<NomenclatureImportReportItem> Report { get; set; } = new();
}

/// <summary>Проверка и транзакционный upsert номенклатуры (PZP-20).</summary>
public sealed class NomenclatureImportService
{
    private readonly ControlDbContext _context;
    private readonly NomenclatureWorkbookParser _parser;

    public NomenclatureImportService(ControlDbContext context, NomenclatureWorkbookParser parser)
    {
        _context = context;
        _parser = parser;
    }

    public async Task<NomenclatureImportResult> ImportAsync(Stream stream, bool skipInvalidRows,
        CancellationToken cancellationToken = default)
    {
        var parsed = _parser.Parse(stream);
        if (parsed.Rows.Count == 0)
            throw new InvalidDataException("В файле нет строк номенклатуры.");

        // Проверка и запись изолированы от параллельных правок справочников: иначе между ними
        // можно добавить изделие, несовместимое с новым минимумом гнёзд ПФ (PZP-36).
        await using var transaction = await _context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var types = await LoadRelevantTypesAsync(parsed.Rows, cancellationToken);
        ResolveExistingTypeArticles(parsed.Rows, types);
        ValidateFileGroups(parsed);
        var molds = await LoadRelevantMoldsAsync(parsed.Rows, cancellationToken);
        await ValidateDatabaseAsync(parsed, types, molds, cancellationToken);
        // После конфликтов в БД в группе ПФ могли остаться только строки с пустой гнёздовостью.
        ValidateFileGroups(parsed);

        var result = new NomenclatureImportResult
        {
            TotalRows = parsed.Rows.Count,
            Report = parsed.Report.OrderBy(r => r.RowNumber).ThenBy(r => r.Message).ToList()
        };
        var rows = parsed.Rows.Where(r => r.CanImport).ToList();
        if (rows.Count != parsed.Rows.Count && !skipInvalidRows)
            return result;
        if (rows.Count == 0)
        {
            result.Status = "Imported";
            result.SkippedRows = parsed.Rows.Count;
            return result;
        }

        var materials = await _context.Materials.ToListAsync(cancellationToken);
        Apply(rows, types, molds, materials, result);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        result.Status = "Imported";
        result.ImportedRows = rows.Count;
        result.SkippedRows = parsed.Rows.Count - rows.Count;
        return result;
    }

    private async Task<List<ProductType>> LoadRelevantTypesAsync(IEnumerable<NomenclatureImportRow> rows,
        CancellationToken cancellationToken)
    {
        var parsedTypeArticles = rows.Select(r => r.ProductTypeArticle).Where(a => a.Length > 0).ToList();
        var fullArticles = rows.Select(r => r.ProductFullArticle).Where(a => a.Length > 0).ToList();
        var formIds = rows.Select(r => r.MoldFormId).Where(a => a.Length > 0).ToList();

        // Точки разрешены и в артикуле типа, и в части изделия. Для повторного импорта
        // заранее находим существующие префиксы, чтобы загрузить самый длинный подходящий тип.
        var existingArticles = await _context.ProductTypes.Select(t => t.Article).ToListAsync(cancellationToken);
        var typeArticles = existingArticles.Where(a => parsedTypeArticles.Contains(a)
            || fullArticles.Any(full => full.StartsWith(a + ".", StringComparison.Ordinal))).ToList();
        typeArticles.AddRange(parsedTypeArticles.Where(a => !typeArticles.Contains(a)));

        return await _context.ProductTypes
            .Include(t => t.Products).ThenInclude(p => p.RecipeIngredients)
            .Include(t => t.Molds)
            .Where(t => typeArticles.Contains(t.Article)
                || t.Products.Any(p => fullArticles.Contains(p.FullArticle))
                || t.Molds.Any(m => formIds.Contains(m.FormId)))
            .ToListAsync(cancellationToken);
    }

    private static void ResolveExistingTypeArticles(IEnumerable<NomenclatureImportRow> rows, IEnumerable<ProductType> types)
    {
        var existingTypes = types.ToList();
        foreach (var row in rows.Where(r => r.ProductFullArticle.Length > 0))
        {
            var exactOwner = existingTypes.FirstOrDefault(t =>
                t.Products.Any(p => p.FullArticle == row.ProductFullArticle));
            var match = exactOwner ?? existingTypes
                .Where(t => row.ProductFullArticle.StartsWith(t.Article + ".", StringComparison.Ordinal))
                .OrderByDescending(t => t.Article.Length)
                .FirstOrDefault();
            if (match == null)
                continue;
            row.ProductTypeArticle = match.Article;
            row.ProductArticle = row.ProductFullArticle[(match.Article.Length + 1)..];
        }
    }

    private async Task<List<Mold>> LoadRelevantMoldsAsync(IEnumerable<NomenclatureImportRow> rows,
        CancellationToken cancellationToken)
    {
        var formIds = rows.Select(r => r.MoldFormId).Where(a => a.Length > 0).Distinct().ToList();
        return await _context.Molds.Include(m => m.ProductType)
            .Where(m => formIds.Contains(m.FormId)).ToListAsync(cancellationToken);
    }

    private async Task ValidateDatabaseAsync(NomenclatureWorkbookParseResult parsed, List<ProductType> types,
        List<Mold> molds, CancellationToken cancellationToken)
    {
        var rows = parsed.Rows.Where(r => r.CanImport).ToList();
        var typeArticles = rows.Select(r => r.ProductTypeArticle).Distinct().ToList();
        var fullArticles = rows.Select(r => r.ProductFullArticle).Distinct().ToList();
        var collidingProducts = await _context.Products.Include(p => p.ProductType)
            .Where(p => typeArticles.Contains(p.FullArticle) || fullArticles.Contains(p.FullArticle))
            .ToListAsync(cancellationToken);
        var collidingTypes = await _context.ProductTypes.Where(t => fullArticles.Contains(t.Article))
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (collidingProducts.Any(p => p.FullArticle == row.ProductTypeArticle)
                || collidingTypes.Any(t => t.Article == row.ProductFullArticle))
                Block(parsed, row, "Артикул конфликтует с существующим типом или изделием.");
            var existingProduct = collidingProducts.FirstOrDefault(p => p.FullArticle == row.ProductFullArticle);
            if (existingProduct != null && existingProduct.ProductType!.Article != row.ProductTypeArticle)
                Block(parsed, row, "Существующее изделие относится к другому типу.");
            var existingMold = molds.FirstOrDefault(m => m.FormId == row.MoldFormId);
            if (existingMold?.ProductTypeId != null && existingMold.ProductType?.Article != row.ProductTypeArticle)
                Block(parsed, row, "Существующая ПФ относится к другому типу изделия.");
        }

        // Конфликты выше могли отфильтровать единственную строку ПФ с числовой гнёздовостью.
        ValidateFileGroups(parsed);
        foreach (var typeGroup in parsed.Rows.Where(r => r.CanImport).GroupBy(r => r.ProductTypeArticle))
        {
            var type = types.FirstOrDefault(t => t.Article == typeGroup.Key);
            var fileMolds = typeGroup.GroupBy(r => r.MoldFormId).ToDictionary(
                g => g.Key, g => g.Max(r => r.Cavities)!.Value);
            var activeMoldMaximums = (type?.Molds ?? new List<Mold>())
                .Where(m => m.IsActive || fileMolds.ContainsKey(m.FormId))
                .Select(m => fileMolds.GetValueOrDefault(m.FormId, m.Cavities))
                .Concat(fileMolds.Where(f => type?.Molds.Any(m => m.FormId == f.Key) != true).Select(f => f.Value))
                .ToList();
            if (activeMoldMaximums.Count == 0)
                continue;
            var minimum = activeMoldMaximums.Min();
            var fileCavities = typeGroup.ToDictionary(r => r.ProductFullArticle, r => r.Cavities);
            // Проверяются все изделия, включая архивные: возврат из архива не должен
            // восстановить умолчание выше уменьшенного максимума ПФ.
            var invalidExisting = (type?.Products ?? new List<Product>()).Any(p =>
                !fileCavities.ContainsKey(p.FullArticle) && p.Cavities > minimum);
            if (invalidExisting || typeGroup.Any(r => r.Cavities > minimum))
            {
                foreach (var row in typeGroup)
                    Block(parsed, row, $"Гнёздовость изделия превышает минимальный максимум активных ПФ типа ({minimum}).");
            }
        }
    }

    private static void ValidateFileGroups(NomenclatureWorkbookParseResult parsed)
    {
        foreach (var group in parsed.Rows.Where(r => r.CanImport).GroupBy(r => r.MoldFormId))
        {
            if (group.Select(r => r.ProductTypeArticle).Distinct().Count() > 1)
            {
                foreach (var row in group)
                    Block(parsed, row, "Одна ПФ относится к разным типам изделий в файле.");
            }
            if (group.Select(r => r.MoldName).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                foreach (var row in group)
                    Block(parsed, row, "Для одной ПФ указаны разные наименования в файле.");
            }
            if (group.All(r => !r.Cavities.HasValue))
            {
                foreach (var row in group)
                    Block(parsed, row, "Для ПФ не указан максимум гнёзд.");
            }
        }
        foreach (var group in parsed.Rows.Where(r => r.CanImport).GroupBy(r => r.ProductTypeArticle))
        {
            if (group.Select(r => r.Customer).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                foreach (var row in group)
                    Block(parsed, row, "Для одного типа изделия указаны разные заказчики.");
            }
        }
    }

    private void Apply(List<NomenclatureImportRow> rows, List<ProductType> types, List<Mold> molds,
        List<Material> materials, NomenclatureImportResult result)
    {
        var moldMaximums = rows.GroupBy(r => r.MoldFormId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.Cavities)!.Value);
        var touchedTypes = new HashSet<Guid>();
        var touchedMolds = new HashSet<Guid>();
        var touchedProducts = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var type = types.FirstOrDefault(t => t.Article == row.ProductTypeArticle);
            if (type == null)
            {
                type = new ProductType { Article = row.ProductTypeArticle, Name = row.ProductName };
                types.Add(type);
                _context.ProductTypes.Add(type);
                touchedTypes.Add(type.Id);
                result.CreatedTypes++;
            }
            else if (touchedTypes.Add(type.Id))
            {
                result.UpdatedTypes++;
            }
            type.Company = row.Customer;
            type.IsActive = true;

            var mold = molds.FirstOrDefault(m => m.FormId == row.MoldFormId);
            if (mold == null)
            {
                mold = new Mold { FormId = row.MoldFormId, ProductTypeId = type.Id };
                molds.Add(mold);
                type.Molds.Add(mold);
                _context.Molds.Add(mold);
                touchedMolds.Add(mold.Id);
                result.CreatedMolds++;
            }
            else
            {
                if (mold.ProductTypeId == null)
                {
                    // Разрешённый доменом переход null → value для старой ПФ.
                    mold.ProductTypeId = type.Id;
                    mold.ProductType = type;
                    type.Molds.Add(mold);
                }
                if (touchedMolds.Add(mold.Id))
                    result.UpdatedMolds++;
            }
            mold.Name = row.MoldName;
            mold.Cavities = moldMaximums[row.MoldFormId];
            mold.IsActive = true;

            var product = type.Products.FirstOrDefault(p => p.FullArticle == row.ProductFullArticle);
            if (product == null)
            {
                product = new Product { ProductTypeId = type.Id, Article = row.ProductArticle, FullArticle = row.ProductFullArticle };
                type.Products.Add(product);
                _context.Products.Add(product);
                touchedProducts.Add(product.Id);
                result.CreatedProducts++;
            }
            else if (touchedProducts.Add(product.Id))
            {
                result.UpdatedProducts++;
            }
            product.Name = row.ProductName;
            product.Comment = row.Comment;
            product.PartWeightGrams = row.PartWeightGrams;
            product.Cavities = row.Cavities;
            product.PlannedInjectionCycleSeconds = row.PlannedInjectionCycleSeconds;
            product.IsActive = true;

            var recipe = row.Recipe.Select(i => new ImportRecipeRow(
                ResolveMaterial(materials, i, result), i.Percent)).ToList();
            foreach (var item in recipe.Select(r => r.Material))
                item.IsActive = true;
            ReplaceRecipe(product, recipe);
        }
    }

    private Material ResolveMaterial(List<Material> materials, NomenclatureRecipeIngredient ingredient,
        NomenclatureImportResult result)
    {
        var sameName = materials.Where(m => m.Type == ingredient.Type
            && string.Equals(m.Name, ingredient.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        var material = sameName.FirstOrDefault(m => m.Manufacturer == null)
            ?? (sameName.Count == 1 ? sameName[0] : null);
        if (material != null)
            return material;

        material = new Material
        {
            Type = ingredient.Type,
            Article = "IMP-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = ingredient.Name,
            ColorName = ingredient.Type == MaterialType.Colorant ? ingredient.Name : null,
            IsActive = true
        };
        materials.Add(material);
        _context.Materials.Add(material);
        result.CreatedMaterials++;
        return material;
    }

    private static void ReplaceRecipe(Product product, IReadOnlyList<ImportRecipeRow> recipe)
    {
        var rows = recipe.Select(r => (MaterialId: r.Material.Id, r.Percent)).ToList();
        var keep = rows.Select(r => r.MaterialId).ToHashSet();
        product.RecipeIngredients.RemoveAll(i => !keep.Contains(i.MaterialId));
        for (var index = 0; index < rows.Count; index++)
        {
            var existing = product.RecipeIngredients.FirstOrDefault(i => i.MaterialId == rows[index].MaterialId);
            if (existing == null)
            {
                product.RecipeIngredients.Add(new ProductRecipeIngredient
                {
                    MaterialId = rows[index].MaterialId, Percent = rows[index].Percent, SortOrder = index
                });
            }
            else
            {
                existing.Percent = rows[index].Percent;
                existing.SortOrder = index;
            }
        }
    }

    private sealed record ImportRecipeRow(Material Material, decimal Percent);

    private static void Block(NomenclatureWorkbookParseResult parsed, NomenclatureImportRow row, string message)
    {
        row.CanImport = false;
        parsed.Report.Add(new NomenclatureImportReportItem(row.RowNumber, row.ProductFullArticle, true, message));
    }
}
