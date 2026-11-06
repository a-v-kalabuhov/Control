using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class Pzp29CatalogApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public Pzp29CatalogApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string Uniq() => $"S{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static async Task<ProductTypeDto> CreateTypeAsync(
        HttpClient client, string article, int? packagingUnit = null)
    {
        var response = await client.PostAsJsonAsync("/api/producttypes", new CreateProductTypeRequestDto
        {
            Article = article, Name = "Тип с образцами", PackagingUnit = packagingUnit
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductTypeDto>())!;
    }

    [Fact]
    public async Task CreateType_StoresPackagingUnit()
    {
        var client = await ClientAsync();

        var type = await CreateTypeAsync(client, Uniq(), 24);

        type.PackagingUnit.Should().Be(24);
    }

    [Fact]
    public async Task UpdateType_ClearsPackagingUnit()
    {
        var client = await ClientAsync();
        var type = await CreateTypeAsync(client, Uniq(), 24);

        var response = await client.PutAsJsonAsync($"/api/producttypes/{type.Id}",
            new UpdateProductTypeRequestDto { ClearPackagingUnit = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{type.Id}");
        updated!.PackagingUnit.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateType_NonPositivePackagingUnit_ReturnsBadRequest(int value)
    {
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/producttypes", new CreateProductTypeRequestDto
        {
            Article = Uniq(), Name = "Тип", PackagingUnit = value
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateType_NonPositivePackagingUnit_ReturnsBadRequest()
    {
        var client = await ClientAsync();
        var type = await CreateTypeAsync(client, Uniq(), 24);

        var response = await client.PutAsJsonAsync($"/api/producttypes/{type.Id}",
            new UpdateProductTypeRequestDto { PackagingUnit = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateType_DefaultProductHasNoSample()
    {
        var client = await ClientAsync();

        var type = await CreateTypeAsync(client, Uniq());

        type.Products.Single().HasSample.Should().BeFalse();
    }

    [Fact]
    public async Task AddProduct_StoresHasSample()
    {
        var client = await ClientAsync();
        var type = await CreateTypeAsync(client, Uniq());

        var response = await client.PostAsJsonAsync($"/api/producttypes/{type.Id}/products",
            new CreateProductRequestDto { Article = "BLK", Name = "Чёрный", HasSample = true });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product!.HasSample.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateProduct_ChangesHasSample()
    {
        var client = await ClientAsync();
        var type = await CreateTypeAsync(client, Uniq());
        var created = await client.PostAsJsonAsync($"/api/producttypes/{type.Id}/products",
            new CreateProductRequestDto { Article = "BLK", Name = "Чёрный", HasSample = true });
        var product = await created.Content.ReadFromJsonAsync<ProductDto>();

        var response = await client.PutAsJsonAsync($"/api/producttypes/{type.Id}/products/{product!.Id}",
            new UpdateProductRequestDto { HasSample = false });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{type.Id}");
        updated!.Products.Single(p => p.Id == product.Id).HasSample.Should().BeFalse();
    }
}
