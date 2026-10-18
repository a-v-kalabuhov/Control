using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductsApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ProductsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string user = "test_manager", string pwd = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, user, pwd);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private async Task<ProductTypeDto> CreateTypeAsync(HttpClient c, string article)
    {
        var r = await c.PostAsJsonAsync("/api/producttypes", new CreateProductTypeRequestDto { Article = article, Name = "Тип" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductTypeDto>())!;
    }

    private static async Task<ProductDto> AddProductAsync(HttpClient c, Guid typeId, string part)
    {
        var r = await c.PostAsJsonAsync($"/api/producttypes/{typeId}/products",
            new CreateProductRequestDto { Article = part, Name = "Чёрный" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await r.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    private static async Task<ProductTypeDto> GetTypeAsync(HttpClient c, Guid id) =>
        (await c.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{id}"))!;

    [Fact]
    public async Task CreateType_HasDefaultProduct()
    {
        var c = await ClientAsync();
        var a = Uniq("T");
        var dto = await CreateTypeAsync(c, a);
        dto.Products.Should().ContainSingle();
        var p = dto.Products[0];
        p.Article.Should().Be("0");
        p.Name.Should().Be("По умолчанию");
        p.FullArticle.Should().Be($"{a}.0");
        p.IsActive.Should().BeTrue();
        p.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task CreateType_NormalizesArticle()
    {
        var c = await ClientAsync();
        var a = Uniq("n");
        var dto = await CreateTypeAsync(c, $" {a.ToLowerInvariant()} ");
        dto.Article.Should().Be(a);
    }

    [Fact]
    public async Task AddProduct_ReturnsCreatedWithFullArticle()
    {
        var c = await ClientAsync();
        var a = Uniq("T");
        var t = await CreateTypeAsync(c, a);
        var r = await c.PostAsJsonAsync($"/api/producttypes/{t.Id}/products",
            new CreateProductRequestDto { Article = "blk", Name = "Чёрный" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var p = await r.Content.ReadFromJsonAsync<ProductDto>();
        p!.FullArticle.Should().Be($"{a}.BLK");
    }

    [Fact]
    public async Task AddProduct_DuplicatePartInType_Conflict()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        await AddProductAsync(c, t.Id, "BLK");
        var r = await c.PostAsJsonAsync($"/api/producttypes/{t.Id}/products",
            new CreateProductRequestDto { Article = "BLK", Name = "Ещё" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddProduct_EmptyPart_BadRequest()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        var r = await c.PostAsJsonAsync($"/api/producttypes/{t.Id}/products",
            new CreateProductRequestDto { Article = "  ", Name = "Ещё" });
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddProduct_FullArticleCollisionAcrossTypes_Conflict()
    {
        var c = await ClientAsync();
        var x = Uniq("C");
        await CreateTypeAsync(c, $"{x}.А");                 // у него есть X.А.0
        var t = await CreateTypeAsync(c, x);
        var r = await c.PostAsJsonAsync($"/api/producttypes/{t.Id}/products",
            new CreateProductRequestDto { Article = "А.0", Name = "Ещё" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync()).Should().Contain($"{x}.А.0");
    }

    [Fact]
    public async Task AddProduct_FullArticleEqualsTypeArticle_Conflict()
    {
        var c = await ClientAsync();
        var y = Uniq("Y");
        await CreateTypeAsync(c, $"{y}.B");
        var t = await CreateTypeAsync(c, y);
        var r = await c.PostAsJsonAsync($"/api/producttypes/{t.Id}/products",
            new CreateProductRequestDto { Article = "B", Name = "Ещё" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateType_ArticleEqualsExistingFullArticle_Conflict()
    {
        var c = await ClientAsync();
        var z = Uniq("Z");
        await CreateTypeAsync(c, z);                         // есть Z.0
        var r = await c.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"{z}.0", Name = "Тип" });
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RenameType_RecomputesFullArticles()
    {
        var c = await ClientAsync();
        var r1 = Uniq("R"); var r2 = Uniq("Q");
        var t = await CreateTypeAsync(c, r1);
        await AddProductAsync(c, t.Id, "BLK");
        var put = await c.PutAsJsonAsync($"/api/producttypes/{t.Id}", new UpdateProductTypeRequestDto { Article = r2 });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var got = await GetTypeAsync(c, t.Id);
        got.Products.Select(p => p.FullArticle).Should().BeEquivalentTo(new[] { $"{r2}.0", $"{r2}.BLK" });
    }

    [Fact]
    public async Task RenameType_TypeArticleEqualsFullArticle_Conflict()
    {
        var c = await ClientAsync();
        var k = Uniq("K"); var m = Uniq("M");
        var tk = await CreateTypeAsync(c, k);
        await AddProductAsync(c, tk.Id, "X");
        var tm = await CreateTypeAsync(c, m);
        await AddProductAsync(c, tm.Id, "N");
        var put = await c.PutAsJsonAsync($"/api/producttypes/{tm.Id}", new UpdateProductTypeRequestDto { Article = $"{k}.X" });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var got = await GetTypeAsync(c, tm.Id);
        got.Article.Should().Be(m);
        got.Products.Select(p => p.FullArticle).Should().Contain($"{m}.N");
    }

    [Fact]
    public async Task RenameType_RecomputedFullArticleCollides_RollsBack()
    {
        var c = await ClientAsync();
        var s = Uniq("S"); var u = Uniq("U");
        await CreateTypeAsync(c, $"{s}.T");                  // S.T.0
        var tu = await CreateTypeAsync(c, u);
        await AddProductAsync(c, tu.Id, "T.0");              // U.T.0
        var put = await c.PutAsJsonAsync($"/api/producttypes/{tu.Id}", new UpdateProductTypeRequestDto { Article = s });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var got = await GetTypeAsync(c, tu.Id);
        got.Article.Should().Be(u);
        got.Products.Select(p => p.FullArticle).Should().BeEquivalentTo(new[] { $"{u}.0", $"{u}.T.0" });
    }

    [Fact]
    public async Task RenameProductPart_RecomputesFullArticle()
    {
        var c = await ClientAsync();
        var a = Uniq("T");
        var t = await CreateTypeAsync(c, a);
        var p = await AddProductAsync(c, t.Id, "BLK");
        var put = await c.PutAsJsonAsync($"/api/producttypes/{t.Id}/products/{p.Id}", new UpdateProductRequestDto { Article = "wht" });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var got = await GetTypeAsync(c, t.Id);
        got.Products.Should().Contain(x => x.Id == p.Id && x.FullArticle == $"{a}.WHT");
    }

    [Fact]
    public async Task ArchiveLastActiveProduct_Conflict()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        var put = await c.PutAsJsonAsync($"/api/producttypes/{t.Id}/products/{t.Products[0].Id}", new UpdateProductRequestDto { IsActive = false });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await put.Content.ReadAsStringAsync()).Should().Contain("Это последнее активное изделие типа. Чтобы вывести тип из оборота, переведите в архив сам тип");
    }

    [Fact]
    public async Task ArchiveProduct_WhenAnotherActive_Ok()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        await AddProductAsync(c, t.Id, "BLK");
        var put = await c.PutAsJsonAsync($"/api/producttypes/{t.Id}/products/{t.Products[0].Id}", new UpdateProductRequestDto { IsActive = false });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var got = await GetTypeAsync(c, t.Id);
        got.Products.Single(p => p.Article == "0").IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ArchivedType_ProductsCanBeArchived()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        (await c.PutAsJsonAsync($"/api/producttypes/{t.Id}", new UpdateProductTypeRequestDto { IsActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var put = await c.PutAsJsonAsync($"/api/producttypes/{t.Id}/products/{t.Products[0].Id}", new UpdateProductRequestDto { IsActive = false });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RestoreType_WithoutActiveProducts_Conflict()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        await c.PutAsJsonAsync($"/api/producttypes/{t.Id}", new UpdateProductTypeRequestDto { IsActive = false });
        await c.PutAsJsonAsync($"/api/producttypes/{t.Id}/products/{t.Products[0].Id}", new UpdateProductRequestDto { IsActive = false });
        var put = await c.PutAsJsonAsync($"/api/producttypes/{t.Id}", new UpdateProductTypeRequestDto { IsActive = true });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await put.Content.ReadAsStringAsync()).Should().Contain("Сначала верните из архива хотя бы одно изделие типа");
    }

    [Fact]
    public async Task DeleteUnusedProduct_Ok()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        var p = await AddProductAsync(c, t.Id, "BLK");
        (await c.DeleteAsync($"/api/producttypes/{t.Id}/products/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetTypeAsync(c, t.Id)).Products.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteLastProduct_Conflict()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        (await c.DeleteAsync($"/api/producttypes/{t.Id}/products/{t.Products[0].Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteUsedProduct_Conflict()
    {
        var c = await ClientAsync();
        var t = await CreateTypeAsync(c, Uniq("T"));
        var p = await AddProductAsync(c, t.Id, "BLK");
        var order = await c.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = Uniq("ORD"), DueDate = DateTime.UtcNow.AddDays(7), ProductId = p.Id, Quantity = 1
        });
        order.StatusCode.Should().Be(HttpStatusCode.Created);

        var del = await c.DeleteAsync($"/api/producttypes/{t.Id}/products/{p.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await del.Content.ReadAsStringAsync()).Should().Contain("Изделие используется в заказах или заданиях — его можно только отправить в архив");
        (await GetTypeAsync(c, t.Id)).Products.Single(x => x.Id == p.Id).IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task Search_FindsByFullArticleOfProduct()
    {
        var c = await ClientAsync();
        var a = Uniq("T");
        var t = await CreateTypeAsync(c, a);
        await AddProductAsync(c, t.Id, "BLK");
        var list = await c.GetFromJsonAsync<List<ProductTypeDto>>($"/api/producttypes?search={a}.BLK");
        list.Should().Contain(x => x.Id == t.Id);
    }

    [Fact]
    public async Task Adjuster_CannotAddProduct()
    {
        var m = await ClientAsync();
        var t = await CreateTypeAsync(m, Uniq("T"));
        var c = await ClientAsync("test_adjuster", "Adjuster123!");
        var r = await c.PostAsJsonAsync($"/api/producttypes/{t.Id}/products",
            new CreateProductRequestDto { Article = "BLK", Name = "Чёрный" });
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
