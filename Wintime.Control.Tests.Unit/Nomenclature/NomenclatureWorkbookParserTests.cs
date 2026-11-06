using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Nomenclature;

namespace Wintime.Control.Tests.Unit.Nomenclature;

public class NomenclatureWorkbookParserTests
{
    [Fact]
    public void Parse_valid_row_reads_all_columns()
    {
        using var stream = Workbook(row =>
        {
            row.Cell(1).Value = "ПФ-01";
            row.Cell(2).Value = "Корпус";
            row.Cell(3).Value = "корпус.чёрный";
            row.Cell(4).Value = "ООО Заказчик";
            row.Cell(5).Value = "Корпус чёрный";
            row.Cell(6).Value = "Не сушить";
            row.Cell(7).Value = 12.5;
            row.Cell(8).Value = 4;
            row.Cell(9).Value = 18;
            row.Cell(10).Value = "ПП";
            row.Cell(11).Value = "Чёрный";
            row.Cell(12).Value = 1.5;
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        var row = result.Rows.Should().ContainSingle().Subject;
        row.RowNumber.Should().Be(2);
        row.MoldFormId.Should().Be("ПФ-01");
        row.MoldName.Should().Be("Корпус");
        row.ProductTypeArticle.Should().Be("КОРПУС");
        row.ProductArticle.Should().Be("ЧЁРНЫЙ");
        row.ProductFullArticle.Should().Be("КОРПУС.ЧЁРНЫЙ");
        row.Customer.Should().Be("ООО Заказчик");
        row.ProductName.Should().Be("Корпус чёрный");
        row.Comment.Should().Be("Не сушить");
        row.PartWeightGrams.Should().Be(12.5m);
        row.Cavities.Should().Be(4);
        row.PlannedInjectionCycleSeconds.Should().Be(18);
        row.Recipe.Should().Equal(
            new NomenclatureRecipeIngredient(MaterialType.Plastic, "ПП", 100m),
            new NomenclatureRecipeIngredient(MaterialType.Colorant, "Чёрный", 1.5m));
        row.CanImport.Should().BeTrue();
        result.Report.Should().BeEmpty();
    }

    [Fact]
    public void Parse_two_plastics_imports_zero_percents_with_nonblocking_warning()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП ; ПЭ";
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        var row = result.Rows.Single();
        row.CanImport.Should().BeTrue();
        row.Recipe.Should().Equal(
            new NomenclatureRecipeIngredient(MaterialType.Plastic, "ПП", 0m),
            new NomenclatureRecipeIngredient(MaterialType.Plastic, "ПЭ", 0m));
        result.Report.Should().ContainSingle(r => r.RowNumber == 2 && !r.BlocksRow)
            .Which.Message.Should().Contain("0 %");
    }

    [Fact]
    public void Parse_ambiguous_recipe_keeps_product_with_empty_recipe_and_report()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "Зависит от заказа";
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        var row = result.Rows.Single();
        row.CanImport.Should().BeTrue();
        row.Recipe.Should().BeEmpty();
        result.Report.Should().ContainSingle(r => r.RowNumber == 2 && !r.BlocksRow)
            .Which.Message.Should().Contain("рецепт");
    }

    [Fact]
    public void Parse_no_colorant_marker_with_zero_percent_keeps_simple_plastic_recipe()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП";
            row.Cell(11).Value = "без красителя";
            row.Cell(12).Value = 0;
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().Recipe.Should().Equal(
            new NomenclatureRecipeIngredient(MaterialType.Plastic, "ПП", 100m));
        result.Report.Should().BeEmpty();
    }

    [Fact]
    public void Parse_colorant_without_percent_clears_recipe_and_reports()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП";
            row.Cell(11).Value = "Синий";
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().Recipe.Should().BeEmpty();
        result.Report.Should().ContainSingle(r => r.RowNumber == 2 && !r.BlocksRow)
            .Which.Message.Should().Contain("красител");
    }

    [Fact]
    public void Parse_missing_weight_and_cycle_uses_domain_defaults()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП";
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        var row = result.Rows.Single();
        row.PartWeightGrams.Should().Be(0m);
        row.Cavities.Should().Be(2);
        row.PlannedInjectionCycleSeconds.Should().BeNull();
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "")]
    [InlineData(3, "БЕЗ-ТОЧКИ")]
    [InlineData(5, "")]
    [InlineData(7, "вес")]
    [InlineData(8, "0")]
    [InlineData(9, "1.5")]
    public void Parse_invalid_required_or_numeric_value_blocks_row(int column, string value)
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(column).Value = value;
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().CanImport.Should().BeFalse();
        result.Report.Should().Contain(r => r.RowNumber == 2 && r.BlocksRow);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1.234)]
    public void Parse_colorant_percent_with_more_than_two_decimals_clears_recipe(double percent)
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП";
            row.Cell(11).Value = "Синий";
            row.Cell(12).Value = percent;
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().Recipe.Should().BeEmpty();
        result.Report.Should().ContainSingle(r => !r.BlocksRow && r.Message.Contains("красител"));
    }

    [Fact]
    public void Parse_too_long_material_name_blocks_row_instead_of_failing_save()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = new string('П', 201);
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().CanImport.Should().BeFalse();
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("200"));
    }

    [Fact]
    public void Parse_too_long_colorant_name_blocks_row_instead_of_failing_save()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП";
            row.Cell(11).Value = new string('С', 101);
            row.Cell(12).Value = 1;
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().CanImport.Should().BeFalse();
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("100"));
    }

    [Fact]
    public void Parse_numeric_overflow_blocks_row_instead_of_throwing()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(7).Value = 1e100;
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().CanImport.Should().BeFalse();
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("Вес"));
    }

    [Fact]
    public void Parse_archive_above_configured_uncompressed_limit_is_rejected()
    {
        using var stream = Workbook(row => FillRequired(row));
        var parser = new NomenclatureWorkbookParser(maxUncompressedBytes: 1);

        var act = () => parser.Parse(stream);

        act.Should().Throw<InvalidDataException>().WithMessage("*размер*");
    }

    [Fact]
    public void Parse_more_rows_than_configured_limit_is_rejected()
    {
        using var stream = Workbook(rows: new[]
        {
            new Action<IXLRow>(row => { FillRequired(row); row.Cell(3).Value = "A.1"; }),
            new Action<IXLRow>(row => { FillRequired(row); row.Cell(3).Value = "A.2"; })
        });
        var parser = new NomenclatureWorkbookParser(maxRows: 1);

        var act = () => parser.Parse(stream);

        act.Should().Throw<InvalidDataException>().WithMessage("*строк*");
    }

    [Fact]
    public void Parse_mixture_with_explicit_percentages_is_not_imported_as_single_plastic()
    {
        using var stream = Workbook(row =>
        {
            FillRequired(row);
            row.Cell(10).Value = "ПП 50%, ПЭ 50%";
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Single().Recipe.Should().BeEmpty();
        result.Report.Should().ContainSingle(r => !r.BlocksRow && r.Message.Contains("Неоднозначный"));
    }

    [Fact]
    public void Parse_malformed_openxml_is_reported_as_invalid_data()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("xl/workbook.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("<workbook><broken>");
        }
        stream.Position = 0;

        var act = () => new NomenclatureWorkbookParser().Parse(stream);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_duplicate_product_with_conflicting_values_blocks_both_rows()
    {
        using var stream = Workbook(rows: new[]
        {
            new Action<IXLRow>(row => { FillRequired(row); row.Cell(5).Value = "Первое"; }),
            new Action<IXLRow>(row => { FillRequired(row); row.Cell(5).Value = "Второе"; })
        });

        var result = new NomenclatureWorkbookParser().Parse(stream);

        result.Rows.Should().OnlyContain(r => !r.CanImport);
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("повтор"));
    }

    private static void FillRequired(IXLRow row)
    {
        row.Cell(1).Value = "ПФ-01";
        row.Cell(2).Value = "Корпус";
        row.Cell(3).Value = "КОРПУС.0";
        row.Cell(5).Value = "Корпус";
        row.Cell(8).Value = 2;
    }

    private static MemoryStream Workbook(Action<IXLRow>? row = null, IEnumerable<Action<IXLRow>>? rows = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Номенклатура");
        string[] headers = ["Артикул ПФ", "Наименование ПФ", "Артикул изделия", "Заказчик",
            "Наименование изделия", "Комментарий", "Вес", "Гнёзда", "Цикл литья", "Материал", "Краситель", "%"];
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        if (row != null)
            row(sheet.Row(2));
        if (rows != null)
        {
            var number = 2;
            foreach (var fill in rows)
                fill(sheet.Row(number++));
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
