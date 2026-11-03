using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Material;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Enums;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductRecipeApiTests : IClassFixture<IntegrationTestFactory>
{
    // Сервер сериализует enum строкой (JsonStringEnumConverter в Program.cs).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public ProductRecipeApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string user = "test_manager", string pwd = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user, pwd);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static async Task<(Guid TypeId, Guid ProductId)> NewTypeAsync(HttpClient c)
    {
        var article = Uniq("R");
        var r = await c.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "Тип " + article });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await r.Content.ReadFromJsonAsync<ProductTypeDto>(JsonOptions))!;
        return (dto.Id, dto.Products[0].Id);
    }

    private static async Task<Guid> NewMaterialAsync(HttpClient c, MaterialType type)
    {
        var article = Uniq("M");
        var r = await c.PostAsJsonAsync("/api/materials", new CreateMaterialRequestDto
        {
            Type = type, Article = article, Name = "Материал " + article,
            ColorName = type == MaterialType.Colorant ? "Чёрный" : null
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<MaterialDto>(JsonOptions))!.Id;
    }

    private static async Task<HttpResponseMessage> PutRecipeAsync(
        HttpClient c, Guid typeId, Guid productId, params (Guid MaterialId, decimal Percent)[] rows)
    {
        var body = new { ingredients = rows.Select(r => new { materialId = r.MaterialId, percent = r.Percent }) };
        return await c.PutAsJsonAsync($"/api/producttypes/{typeId}/products/{productId}/recipe", body);
    }

    private static async Task<ProductTypeDto> GetTypeAsync(HttpClient c, Guid id) =>
        (await c.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{id}", JsonOptions))!;

    [Fact]
    public async Task PutRecipe_SavesWholeSetInArrayOrder()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);
        var a = await NewMaterialAsync(c, MaterialType.Plastic);
        var b = await NewMaterialAsync(c, MaterialType.Plastic);

        var resp = await PutRecipeAsync(c, typeId, productId, (b, 40m), (a, 60m));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var dto = (await resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;
        dto.Recipe.Select(r => r.MaterialId).Should().Equal(b, a);
        dto.Recipe.All(r => r.MaterialType == MaterialType.Plastic).Should().BeTrue();
        dto.Recipe.All(r => !string.IsNullOrEmpty(r.MaterialArticle)).Should().BeTrue();

        var type = await GetTypeAsync(c, typeId);
        type.Products.Single(p => p.Id == productId).Recipe.Select(r => r.MaterialId).Should().Equal(b, a);
    }

    [Fact]
    public async Task PutRecipe_ResaveSameMaterial_UpdatesRowInPlace()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);
        var a = await NewMaterialAsync(c, MaterialType.Plastic);
        var b = await NewMaterialAsync(c, MaterialType.Plastic);
        (await PutRecipeAsync(c, typeId, productId, (a, 60m), (b, 40m))).StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await PutRecipeAsync(c, typeId, productId, (a, 90m));

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var type = await GetTypeAsync(c, typeId);
        type.Products.Single(p => p.Id == productId).Recipe
            .Should().ContainSingle().Which.Percent.Should().Be(90m);
    }

    [Fact]
    public async Task PutRecipe_EmptyArray_ClearsRecipe()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);
        var a = await NewMaterialAsync(c, MaterialType.Plastic);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await PutRecipeAsync(c, typeId, productId);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!.Recipe.Should().BeEmpty();
        (await GetTypeAsync(c, typeId)).Products.Single(p => p.Id == productId).Recipe.Should().BeEmpty();
    }

    [Fact]
    public async Task PutRecipe_PlasticSumNot100_Returns200()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);
        var a = await NewMaterialAsync(c, MaterialType.Plastic);

        var resp = await PutRecipeAsync(c, typeId, productId, (a, 98m));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutRecipe_UnknownMaterial_Returns404()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);

        var resp = await PutRecipeAsync(c, typeId, productId, (Guid.NewGuid(), 100m));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutRecipe_WithoutPlastic_Returns400()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);
        var colorant = await NewMaterialAsync(c, MaterialType.Colorant);

        var resp = await PutRecipeAsync(c, typeId, productId, (colorant, 2m));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutRecipe_MoreThan8Rows_Returns400()
    {
        var c = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(c);
        var ids = new List<(Guid, decimal)>();
        for (var i = 0; i < 9; i++)
            ids.Add((await NewMaterialAsync(c, MaterialType.Plastic), 11m));

        var resp = await PutRecipeAsync(c, typeId, productId, ids.ToArray());

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutRecipe_ProductOfAnotherType_Returns404()
    {
        var c = await ClientAsync();
        var (typeId, _) = await NewTypeAsync(c);
        var (_, otherProductId) = await NewTypeAsync(c);
        var a = await NewMaterialAsync(c, MaterialType.Plastic);

        var resp = await PutRecipeAsync(c, typeId, otherProductId, (a, 100m));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PutRecipe_Adjuster_Returns403()
    {
        var manager = await ClientAsync();
        var (typeId, productId) = await NewTypeAsync(manager);
        var a = await NewMaterialAsync(manager, MaterialType.Plastic);
        var adjuster = await ClientAsync("test_adjuster", "Adjuster123!");

        var resp = await PutRecipeAsync(adjuster, typeId, productId, (a, 100m));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
