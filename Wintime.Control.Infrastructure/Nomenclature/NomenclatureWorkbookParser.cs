using System.Globalization;
using System.IO.Compression;
using System.Xml;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Infrastructure.Nomenclature;

public sealed record NomenclatureRecipeIngredient(MaterialType Type, string Name, decimal Percent);

public sealed class NomenclatureImportRow
{
    public int RowNumber { get; set; }
    public string MoldFormId { get; set; } = string.Empty;
    public string MoldName { get; set; } = string.Empty;
    public string ProductTypeArticle { get; set; } = string.Empty;
    public string ProductArticle { get; set; } = string.Empty;
    public string ProductFullArticle { get; set; } = string.Empty;
    public string? Customer { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public decimal PartWeightGrams { get; set; }
    public int? Cavities { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
    public List<NomenclatureRecipeIngredient> Recipe { get; set; } = new();
    public bool CanImport { get; set; } = true;
}

public sealed record NomenclatureImportReportItem(int RowNumber, string FullArticle, bool BlocksRow, string Message);

public sealed class NomenclatureWorkbookParseResult
{
    public List<NomenclatureImportRow> Rows { get; set; } = new();
    public List<NomenclatureImportReportItem> Report { get; set; } = new();
}

/// <summary>Разбор согласованного XLSX A:L без выполнения макросов и внешних связей (PZP-20).</summary>
public sealed class NomenclatureWorkbookParser
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private readonly long _maxUncompressedBytes;
    private readonly int _maxRows;

    public NomenclatureWorkbookParser(long maxUncompressedBytes = 100 * 1024 * 1024, int maxRows = 10000)
    {
        _maxUncompressedBytes = maxUncompressedBytes;
        _maxRows = maxRows;
    }

    public NomenclatureWorkbookParseResult Parse(Stream stream)
    {
        if (!stream.CanSeek)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            stream = copy;
        }
        ValidateArchive(stream);
        using var workbook = CreateWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidDataException("В файле нет листа с номенклатурой.");
        var result = new NomenclatureWorkbookParseResult();
        var usedRows = sheet.RowsUsed().Where(r => r.RowNumber() > 1).ToList();
        if (usedRows.Count > _maxRows)
            throw new InvalidDataException($"В файле больше {_maxRows} строк номенклатуры.");

        foreach (var excelRow in usedRows)
        {
            if (Enumerable.Range(1, 12).All(c => string.IsNullOrWhiteSpace(Text(excelRow.Cell(c)))))
                continue;

            var row = new NomenclatureImportRow
            {
                RowNumber = excelRow.RowNumber(),
                MoldFormId = Text(excelRow.Cell(1)),
                MoldName = Text(excelRow.Cell(2)),
                Customer = Clean(excelRow.Cell(4)),
                ProductName = Text(excelRow.Cell(5)),
                Comment = Clean(excelRow.Cell(6)),
                Cavities = null
            };
            result.Rows.Add(row);

            if (row.MoldFormId.Length == 0)
                Block(result, row, "Не указан артикул ПФ.");
            if (row.MoldName.Length == 0)
                Block(result, row, "Не указано наименование ПФ.");
            if (row.ProductName.Length == 0)
                Block(result, row, "Не указано наименование изделия.");

            var fullArticle = ProductArticle.Normalize(Text(excelRow.Cell(3)));
            var separator = fullArticle.IndexOf('.');
            if (separator <= 0 || separator == fullArticle.Length - 1)
            {
                Block(result, row, "Полный артикул изделия должен иметь вид ТИП.ЧАСТЬ.");
            }
            else
            {
                row.ProductTypeArticle = fullArticle[..separator];
                row.ProductArticle = fullArticle[(separator + 1)..];
                row.ProductFullArticle = ProductArticle.Compose(row.ProductTypeArticle, row.ProductArticle);
            }

            if (!IsEmpty(excelRow.Cell(7)))
            {
                if (TryDecimal(excelRow.Cell(7), out var weight))
                {
                    if (weight < 0)
                        Block(result, row, "Вес детали должен быть неотрицательным.");
                    else
                        row.PartWeightGrams = weight;
                }
                else
                {
                    Block(result, row, "Вес детали должен быть числом.");
                }
            }

            if (IsEmpty(excelRow.Cell(8)))
            {
                row.Cavities = null;
            }
            else if (TryInteger(excelRow.Cell(8), out var cavities) && cavities > 0)
            {
                row.Cavities = cavities;
            }
            else
            {
                Block(result, row, "Гнёздовость должна быть положительным целым числом.");
            }

            if (!IsEmpty(excelRow.Cell(9)))
            {
                if (TryInteger(excelRow.Cell(9), out var cycle) && cycle > 0)
                    row.PlannedInjectionCycleSeconds = cycle;
                else
                    Block(result, row, "Цикл литья должен быть положительным целым числом секунд.");
            }

            ParseRecipe(result, row, Text(excelRow.Cell(10)), Text(excelRow.Cell(11)), excelRow.Cell(12));
        }

        foreach (var duplicate in result.Rows.GroupBy(r => r.ProductFullArticle).Where(g => g.Key.Length > 0 && g.Count() > 1))
        {
            foreach (var row in duplicate)
                Block(result, row, $"Артикул {duplicate.Key} повторяется в файле.");
        }

        return result;
    }

    private static void ParseRecipe(NomenclatureWorkbookParseResult result, NomenclatureImportRow row,
        string material, string colorant, IXLCell percentCell)
    {
        if (material.Length == 0 && colorant.Length == 0 && IsEmpty(percentCell))
            return;

        var hasPercent = !IsEmpty(percentCell);
        var noColorant = colorant.Equals("без красителя", StringComparison.OrdinalIgnoreCase)
            || colorant.Equals("нет", StringComparison.OrdinalIgnoreCase);
        if (noColorant)
        {
            if (hasPercent && (!TryDecimal(percentCell, out var markedPercent) || markedPercent != 0))
            {
                Warn(result, row, "Отметке «без красителя» соответствует ненулевой процент; рецепт требует ручной доработки.");
                return;
            }
            colorant = string.Empty;
            hasPercent = false;
        }

        var plasticNames = material.Split(';', StringSplitOptions.TrimEntries);
        if (plasticNames.Any(name => name.Length > MaterialRules.NameMaxLength))
        {
            Block(result, row, $"Наименование материала длиннее {MaterialRules.NameMaxLength} символов.");
            return;
        }
        if (colorant.Length > MaterialRules.ColorNameMaxLength)
        {
            Block(result, row, $"Наименование красителя длиннее {MaterialRules.ColorNameMaxLength} символов.");
            return;
        }
        var ambiguous = material.Length == 0
            || material.Contains("зависит", StringComparison.OrdinalIgnoreCase)
            || material.Contains("без сырья", StringComparison.OrdinalIgnoreCase)
            || material.Contains('%')
            || material.Contains(',')
            || material.Contains('/')
            || plasticNames.Length > 2
            || plasticNames.Any(string.IsNullOrWhiteSpace)
            || plasticNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != plasticNames.Length;

        var percentValid = !hasPercent || (TryDecimal(percentCell, out var parsedPercent)
            && parsedPercent >= 0.01m && parsedPercent <= 100 && decimal.Round(parsedPercent, 2) == parsedPercent);
        if (colorant.Length > 0 && (!hasPercent || !percentValid))
        {
            Warn(result, row, "Не указан корректный процент красителя; рецепт требует ручной доработки.");
            return;
        }
        if (colorant.Length == 0 && hasPercent)
        {
            Warn(result, row, "Процент указан без красителя; рецепт требует ручной доработки.");
            return;
        }
        if (ambiguous)
        {
            Warn(result, row, "Неоднозначный материал; изделие импортируется с пустым рецептом.");
            return;
        }

        var plastics = plasticNames.Length == 2
            ? plasticNames.Select(name => new NomenclatureRecipeIngredient(MaterialType.Plastic, name, 0m)).ToList()
            : [new NomenclatureRecipeIngredient(MaterialType.Plastic, plasticNames[0], 100m)];
        row.Recipe.AddRange(plastics);
        if (colorant.Length > 0)
        {
            TryDecimal(percentCell, out var colorantPercent);
            row.Recipe.Add(new NomenclatureRecipeIngredient(MaterialType.Colorant, colorant, colorantPercent));
        }
        if (plasticNames.Length == 2)
            Warn(result, row, "Два пластика импортированы с долей 0 %; укажите пропорции вручную.");
    }

    private static void Block(NomenclatureWorkbookParseResult result, NomenclatureImportRow row, string message)
    {
        row.CanImport = false;
        result.Report.Add(new NomenclatureImportReportItem(row.RowNumber, row.ProductFullArticle, true, message));
    }

    private static void Warn(NomenclatureWorkbookParseResult result, NomenclatureImportRow row, string message) =>
        result.Report.Add(new NomenclatureImportReportItem(row.RowNumber, row.ProductFullArticle, false, message));

    private static string Text(IXLCell cell) => cell.GetFormattedString().Trim();
    private static string? Clean(IXLCell cell) => MaterialRules.Clean(Text(cell));
    private static bool IsEmpty(IXLCell cell) => string.IsNullOrWhiteSpace(Text(cell));

    private static XLWorkbook CreateWorkbook(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception e) when (e is XmlException or FileFormatException or ArgumentException
            or NotSupportedException or NullReferenceException or InvalidOperationException)
        {
            throw new InvalidDataException("Файл повреждён или не является поддерживаемым XLSX.", e);
        }
    }

    private void ValidateArchive(Stream stream)
    {
        stream.Position = 0;
        long uncompressedBytes = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                if (entry.Length > _maxUncompressedBytes - uncompressedBytes)
                    throw new InvalidDataException("Распакованный размер XLSX превышает допустимый предел.");
                uncompressedBytes += entry.Length;
            }
        }
        stream.Position = 0;
    }

    private static bool TryDecimal(IXLCell cell, out decimal value)
    {
        if (cell.DataType == XLDataType.Number && cell.TryGetValue<double>(out var number))
        {
            try
            {
                value = (decimal)number;
                return true;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }
        return decimal.TryParse(Text(cell), NumberStyles.Any, Russian, out value)
            || decimal.TryParse(Text(cell), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryInteger(IXLCell cell, out int value)
    {
        value = 0;
        if (!TryDecimal(cell, out var number) || number != decimal.Truncate(number) || number > int.MaxValue || number < int.MinValue)
            return false;
        value = (int)number;
        return true;
    }
}
