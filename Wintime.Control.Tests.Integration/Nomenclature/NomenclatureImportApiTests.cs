using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Nomenclature;

[Collection("Integration")]
public class NomenclatureImportApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public NomenclatureImportApiTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Clean_file_imports_immediately_with_recipe_and_comment()
    {
        var c = await ClientAsync();
        var data = UniqueData();

        var response = await Upload(c, data.Type, data.Mold, row: row =>
        {
            row.Cell(4).Value = "ООО Заказчик";
            row.Cell(6).Value = "Не сушить";
            row.Cell(7).Value = 12.5;
            row.Cell(8).Value = 4;
            row.Cell(9).Value = 18;
            row.Cell(10).Value = "ПП";
            row.Cell(11).Value = "Чёрный";
            row.Cell(12).Value = 1.5;
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        result!.Status.Should().Be("Imported");
        result.ImportedRows.Should().Be(1);
        result.SkippedRows.Should().Be(0);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var type = await db.ProductTypes.Include(t => t.Molds).Include(t => t.Products)
            .ThenInclude(p => p.RecipeIngredients).ThenInclude(i => i.Material)
            .SingleAsync(t => t.Article == data.Type);
        type.Company.Should().Be("ООО Заказчик");
        type.Molds.Single().FormId.Should().Be(data.Mold);
        type.Molds.Single().Cavities.Should().Be(4);
        var product = type.Products.Single();
        product.Name.Should().Be("Изделие");
        product.Comment.Should().Be("Не сушить");
        product.PartWeightGrams.Should().Be(12.5m);
        product.Cavities.Should().Be(4);
        product.PlannedInjectionCycleSeconds.Should().Be(18);
        product.RecipeIngredients.Should().HaveCount(2);
        product.RecipeIngredients.Single(i => i.Material!.Type == MaterialType.Plastic).Percent.Should().Be(100);
        product.RecipeIngredients.Single(i => i.Material!.Type == MaterialType.Colorant).Percent.Should().Be(1.5m);
        product.RecipeIngredients.Single(i => i.Material!.Type == MaterialType.Colorant).Material!.ColorName.Should().Be("Чёрный");
    }

    [Fact]
    public async Task Structural_error_requires_confirmation_then_skip_imports_only_valid_rows()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        byte[] Workbook() => WorkbookBytes(data.Type, data.Mold,
            row => row.Cell(3).Value = data.Type + ".OK",
            row => { row.Cell(3).Value = data.Type + ".BAD"; row.Cell(5).Value = ""; });

        var preview = await Upload(c, Workbook());
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var previewResult = await preview.Content.ReadFromJsonAsync<ImportResponse>();
        previewResult!.Status.Should().Be("ConfirmationRequired");
        previewResult.ImportedRows.Should().Be(0);
        previewResult.Report.Should().Contain(r => r.BlocksRow);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ProductTypes.AnyAsync(t => t.Article == data.Type)).Should().BeFalse();
        }

        var applied = await Upload(c, Workbook(), skipInvalidRows: true);
        var appliedResult = await applied.Content.ReadFromJsonAsync<ImportResponse>();
        appliedResult!.Status.Should().Be("Imported");
        appliedResult.ImportedRows.Should().Be(1);
        appliedResult.SkippedRows.Should().Be(1);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var products = await verifyDb.Products.Where(p => p.ProductType!.Article == data.Type).ToListAsync();
        products.Should().ContainSingle(p => p.FullArticle == data.Type + ".OK");
    }

    [Fact]
    public async Task Reimport_updates_matches_and_preserves_products_missing_from_file()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var initial = WorkbookBytes(data.Type, data.Mold,
            row => row.Cell(3).Value = data.Type + ".ONE",
            row => { row.Cell(3).Value = data.Type + ".TWO"; row.Cell(5).Value = "Второе"; });
        (await Upload(c, initial)).StatusCode.Should().Be(HttpStatusCode.OK);
        Guid productId;
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            productId = (await seedDb.Products.SingleAsync(p => p.FullArticle == data.Type + ".ONE")).Id;
        }

        var repeated = WorkbookBytes(data.Type, data.Mold, row =>
        {
            row.Cell(3).Value = data.Type + ".ONE";
            row.Cell(5).Value = "Обновлённое";
            row.Cell(7).Value = 9.5;
            row.Cell(8).Value = 3;
            row.Cell(10).Value = "ПЭ";
        });
        var response = await Upload(c, repeated);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var products = await db.Products.Include(p => p.RecipeIngredients).ThenInclude(i => i.Material)
            .Where(p => p.ProductType!.Article == data.Type).ToListAsync();
        products.Should().HaveCount(2);
        products.Single(p => p.FullArticle == data.Type + ".TWO").Name.Should().Be("Второе");
        var updated = products.Single(p => p.FullArticle == data.Type + ".ONE");
        updated.Id.Should().Be(productId);
        updated.Name.Should().Be("Обновлённое");
        updated.PartWeightGrams.Should().Be(9.5m);
        updated.Cavities.Should().Be(3);
        updated.RecipeIngredients.Should().ContainSingle(i => i.Material!.Name == "ПЭ" && i.Percent == 100);
        (await db.Molds.CountAsync(m => m.FormId == data.Mold)).Should().Be(1);
    }

    [Fact]
    public async Task Two_plastics_and_ambiguous_material_follow_their_reporting_rules()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold,
            row => { row.Cell(3).Value = data.Type + ".PAIR"; row.Cell(10).Value = "ПП;ПЭ"; },
            row => { row.Cell(3).Value = data.Type + ".UNKNOWN"; row.Cell(10).Value = "Зависит от заказа"; }));

        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        result!.Status.Should().Be("Imported");
        result.ImportedRows.Should().Be(2);
        result.Report.Should().HaveCount(2);
        result.Report.Should().OnlyContain(r => !r.BlocksRow);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var products = await db.Products.Include(p => p.RecipeIngredients)
            .Where(p => p.ProductType!.Article == data.Type).ToListAsync();
        products.Single(p => p.FullArticle == data.Type + ".PAIR").RecipeIngredients
            .Should().OnlyContain(i => i.Percent == 0).And.HaveCount(2);
        products.Single(p => p.FullArticle == data.Type + ".UNKNOWN").RecipeIngredients.Should().BeEmpty();
    }

    [Fact]
    public async Task Reimport_matches_existing_material_case_insensitively()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var materialName = "ПП-" + data.Mold;
        Guid materialId;
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var material = new Material { Type = MaterialType.Plastic, Article = data.Mold, Name = materialName };
            seedDb.Materials.Add(material);
            await seedDb.SaveChangesAsync();
            materialId = material.Id;
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold, row => row.Cell(10).Value = materialName.ToLowerInvariant()));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var ingredient = await db.ProductRecipeIngredients.Include(i => i.Product)
            .SingleAsync(i => i.Product!.FullArticle == data.Type + ".0");
        ingredient.MaterialId.Should().Be(materialId);
    }

    [Fact]
    public async Task Ambiguous_existing_manufacturers_create_unqualified_material_instead_of_guessing()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            seedDb.Materials.AddRange(
                new Material { Type = MaterialType.Plastic, Article = data.Mold + "A", Name = "ПП", Manufacturer = "A" },
                new Material { Type = MaterialType.Plastic, Article = data.Mold + "B", Name = "ПП", Manufacturer = "B" });
            await seedDb.SaveChangesAsync();
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold, row => row.Cell(10).Value = "ПП"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var ingredient = await db.ProductRecipeIngredients.Include(i => i.Product).Include(i => i.Material)
            .SingleAsync(i => i.Product!.FullArticle == data.Type + ".0");
        ingredient.Material!.Manufacturer.Should().BeNull();
    }

    [Fact]
    public async Task Mold_maximum_and_product_cavities_are_imported_separately()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold,
            row => { row.Cell(3).Value = data.Type + ".FOUR"; row.Cell(8).Value = 4; },
            row => { row.Cell(3).Value = data.Type + ".TWO"; row.Cell(8).Value = 2; }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.Molds.SingleAsync(m => m.FormId == data.Mold)).Cavities.Should().Be(4);
        var products = await db.Products.Where(p => p.ProductType!.Article == data.Type).ToListAsync();
        products.Single(p => p.FullArticle == data.Type + ".FOUR").Cavities.Should().Be(4);
        products.Single(p => p.FullArticle == data.Type + ".TWO").Cavities.Should().Be(2);
    }

    [Fact]
    public async Task Existing_mold_of_other_type_requires_confirmation_and_preserves_it()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        Guid otherTypeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var otherType = new ProductType { Article = "OTHER" + data.Type, Name = "Другой тип" };
            otherType.Molds.Add(new Mold { FormId = data.Mold, Name = "Чужая ПФ", Cavities = 9 });
            seedDb.ProductTypes.Add(otherType);
            await seedDb.SaveChangesAsync();
            otherTypeId = otherType.Id;
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold));
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        result!.Status.Should().Be("ConfirmationRequired");
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("другому типу"));

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = await db.Molds.SingleAsync(m => m.FormId == data.Mold);
        mold.ProductTypeId.Should().Be(otherTypeId);
        mold.Cavities.Should().Be(9);
        (await db.ProductTypes.AnyAsync(t => t.Article == data.Type)).Should().BeFalse();
    }

    [Fact]
    public async Task Mold_reduction_conflicting_with_existing_product_default_requires_confirmation()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var type = new ProductType { Article = data.Type, Name = "Существующий тип" };
            type.Molds.Add(new Mold { FormId = data.Mold, Name = "ПФ", Cavities = 4 });
            type.Products.Add(new Product
            {
                Article = "OLD", FullArticle = data.Type + ".OLD", Name = "Старое", Cavities = 4
            });
            seedDb.ProductTypes.Add(type);
            await seedDb.SaveChangesAsync();
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold, row =>
        {
            row.Cell(3).Value = data.Type + ".NEW";
            row.Cell(8).Value = 2;
        }));
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        result!.Status.Should().Be("ConfirmationRequired");
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("минимальный максимум"));

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.Molds.SingleAsync(m => m.FormId == data.Mold)).Cavities.Should().Be(4);
        (await db.Products.AnyAsync(p => p.FullArticle == data.Type + ".NEW")).Should().BeFalse();
    }

    [Fact]
    public async Task Different_customers_for_one_type_are_not_guessed()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold,
            row => { row.Cell(3).Value = data.Type + ".ONE"; row.Cell(4).Value = "Первый"; },
            row => { row.Cell(3).Value = data.Type + ".TWO"; row.Cell(4).Value = "Второй"; }));
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();

        result!.Status.Should().Be("ConfirmationRequired");
        result.ImportedRows.Should().Be(0);
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("разные заказчики"));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ProductTypes.AnyAsync(t => t.Article == data.Type)).Should().BeFalse();
    }

    [Fact]
    public async Task Archived_product_default_also_blocks_mold_reduction()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var type = new ProductType { Article = data.Type, Name = "Тип" };
            type.Molds.Add(new Mold { FormId = data.Mold, Name = "ПФ", Cavities = 4 });
            type.Products.Add(new Product
            {
                Article = "OLD", FullArticle = data.Type + ".OLD", Name = "Архивное", Cavities = 4, IsActive = false
            });
            seedDb.ProductTypes.Add(type);
            await seedDb.SaveChangesAsync();
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold, row =>
        {
            row.Cell(3).Value = data.Type + ".NEW";
            row.Cell(8).Value = 2;
        }));
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();

        result!.Status.Should().Be("ConfirmationRequired");
        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.Molds.SingleAsync(m => m.FormId == data.Mold)).Cavities.Should().Be(4);
    }

    [Fact]
    public async Task Orphan_mold_is_attached_to_imported_type_without_duplicate()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var seedMold = new Mold { FormId = data.Mold, Name = "Старая ПФ", Cavities = 1, ProductTypeId = null };
            seedDb.Molds.Add(seedMold);
            await seedDb.SaveChangesAsync();
            moldId = seedMold.Id;
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = await db.Molds.Include(m => m.ProductType).SingleAsync(m => m.FormId == data.Mold);
        mold.Id.Should().Be(moldId);
        mold.ProductType!.Article.Should().Be(data.Type);
        mold.Cavities.Should().Be(2);
    }

    [Fact]
    public async Task Null_cavities_left_after_other_row_was_blocked_returns_preview_not_500()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ProductTypes.Add(new ProductType { Article = data.Type + ".A", Name = "Существующий тип" });
            await db.SaveChangesAsync();
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold,
            row => { row.Cell(3).Value = data.Type + ".A"; row.Cell(8).Value = 4; },
            row => { row.Cell(3).Value = data.Type + ".B"; row.Cell(8).Clear(); }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();
        result!.Status.Should().Be("ConfirmationRequired");
        result.Report.Should().Contain(r => r.BlocksRow);
    }

    [Fact]
    public async Task Existing_type_with_dot_receives_new_product_and_repeat_upsert()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var typeArticle = data.Type + ".SUB";
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var type = new ProductType { Article = typeArticle, Name = "Тип с точкой" };
            type.Products.Add(new Product { Article = "0", FullArticle = typeArticle + ".0", Name = "По умолчанию" });
            seedDb.ProductTypes.Add(type);
            await seedDb.SaveChangesAsync();
        }

        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold, row =>
            row.Cell(3).Value = typeArticle + ".RED"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ProductTypes.AnyAsync(t => t.Article == data.Type)).Should().BeFalse();
        var product = await db.Products.Include(p => p.ProductType).SingleAsync(p => p.FullArticle == typeArticle + ".RED");
        product.ProductType!.Article.Should().Be(typeArticle);
        product.Article.Should().Be("RED");
    }

    [Fact]
    public async Task Exact_existing_product_wins_over_longer_type_prefix()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var shortType = data.Type;
        var longType = data.Type + ".B";
        var fullArticle = longType + ".C";
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var owner = new ProductType { Article = shortType, Name = "Короткий тип" };
            owner.Products.Add(new Product { Article = "B.C", FullArticle = fullArticle, Name = "Исходное" });
            owner.Molds.Add(new Mold { FormId = data.Mold, Name = "ПФ", Cavities = 2 });
            var other = new ProductType { Article = longType, Name = "Длинный тип" };
            other.Products.Add(new Product { Article = "0", FullArticle = longType + ".0", Name = "По умолчанию" });
            seedDb.ProductTypes.AddRange(owner, other);
            await seedDb.SaveChangesAsync();
        }

        var response = await Upload(c, WorkbookBytes(shortType, data.Mold, row =>
        {
            row.Cell(3).Value = fullArticle;
            row.Cell(5).Value = "Обновлённое";
        }));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var verify = _factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var product = await db.Products.Include(p => p.ProductType).SingleAsync(p => p.FullArticle == fullArticle);
        product.ProductType!.Article.Should().Be(shortType);
        product.Name.Should().Be("Обновлённое");
    }

    [Fact]
    public async Task Conflicting_mold_names_in_file_require_confirmation()
    {
        var c = await ClientAsync();
        var data = UniqueData();
        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold,
            row => { row.Cell(3).Value = data.Type + ".ONE"; row.Cell(2).Value = "Первая ПФ"; },
            row => { row.Cell(3).Value = data.Type + ".TWO"; row.Cell(2).Value = "Вторая ПФ"; }));
        var result = await response.Content.ReadFromJsonAsync<ImportResponse>();

        result!.Status.Should().Be("ConfirmationRequired");
        result.Report.Should().Contain(r => r.BlocksRow && r.Message.Contains("наименования"));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ProductTypes.AnyAsync(t => t.Article == data.Type)).Should().BeFalse();
    }

    [Fact]
    public async Task Non_xlsx_file_is_rejected()
    {
        var c = await ClientAsync();
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent([1, 2, 3]);
        content.Add(file, "file", "nomen.xls");
        var response = await c.PostAsync("/api/nomenclature-import", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Observer_cannot_import()
    {
        var c = await ClientAsync("test_observer", "Observer123!");
        var data = UniqueData();
        var response = await Upload(c, WorkbookBytes(data.Type, data.Mold));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> ClientAsync(string user = "test_manager", string password = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static (string Type, string Mold) UniqueData()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        return ("IMP" + suffix, "FORM" + suffix);
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, string type, string mold,
        Action<IXLRow>? row = null, bool skipInvalidRows = false) =>
        await Upload(client, WorkbookBytes(type, mold, row), skipInvalidRows);

    private static async Task<HttpResponseMessage> Upload(HttpClient client, byte[] bytes, bool skipInvalidRows = false)
    {
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(file, "file", "nomen.xlsx");
        content.Add(new StringContent(skipInvalidRows.ToString()), "skipInvalidRows");
        return await client.PostAsync("/api/nomenclature-import", content);
    }

    private static byte[] WorkbookBytes(string type, string mold, params Action<IXLRow>?[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Номенклатура");
        string[] headers = ["Артикул ПФ", "Наименование ПФ", "Артикул изделия", "Заказчик",
            "Наименование изделия", "Комментарий", "Вес", "Гнёзда", "Цикл литья", "Материал", "Краситель", "%"];
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];
        if (rows.Length == 0)
            rows = [null];
        for (var i = 0; i < rows.Length; i++)
        {
            var row = sheet.Row(i + 2);
            row.Cell(1).Value = mold;
            row.Cell(2).Value = "Пресс-форма";
            row.Cell(3).Value = type + ".0";
            row.Cell(5).Value = "Изделие";
            row.Cell(8).Value = 2;
            rows[i]?.Invoke(row);
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private sealed class ImportResponse
    {
        public string Status { get; set; } = string.Empty;
        public int ImportedRows { get; set; }
        public int SkippedRows { get; set; }
        public List<ReportItem> Report { get; set; } = new();
    }

    private sealed class ReportItem
    {
        public int RowNumber { get; set; }
        public bool BlocksRow { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
