using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldProductTypeTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldProductTypeTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedTypeAsync(bool isActive = true, string? company = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var article = $"PT-{Guid.NewGuid():N}".ToUpperInvariant();
        var pt = new ProductType { Article = article, Name = "Изд", Company = company, IsActive = isActive };
        pt.Products.Add(new Product { Article = "0", Name = "По умолчанию", FullArticle = article + ".0" });   // PZP-19
        db.ProductTypes.Add(pt);
        await db.SaveChangesAsync();
        return pt.Id;
    }

    private static CreateMoldRequestDto NewMold(Guid? typeId) => new()
    {
        FormId = $"PF-{Guid.NewGuid():N}", Name = "ПФ", Cavities = 1,
        MaxResourceCycles = 1000, ProductTypeId = typeId
    };

    [Fact]
    public async Task CreateMold_WithoutProductType_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(null));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithArchivedType_Returns400()
    {
        var client = await ManagerClientAsync();
        var archived = await SeedTypeAsync(isActive: false);
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(archived));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithType_BindsAndDtoHasTypeName()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync(company: "ООО Заказчик");
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(typeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<MoldDto>();
        dto!.ProductTypeId.Should().Be(typeId);
        dto.ProductTypeName.Should().Be("Изд");
        dto.ProductTypeCompany.Should().Be("ООО Заказчик");
    }

    [Fact]
    public async Task CreateMold_CavitiesBelowProductDefault_Returns400()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        var type = await client.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{typeId}");
        var productUpdate = await client.PutAsJsonAsync($"/api/producttypes/{typeId}/products/{type!.Products[0].Id}",
            new { Cavities = 4 });
        productUpdate.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var request = NewMold(typeId);
        request.Cavities = 2;

        var response = await client.PostAsJsonAsync("/api/molds", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMold_CavitiesBelowProductDefault_Returns400()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        var createRequest = NewMold(typeId);
        createRequest.Cavities = 4;
        var created = await client.PostAsJsonAsync("/api/molds", createRequest);
        var mold = await created.Content.ReadFromJsonAsync<MoldDto>();
        var type = await client.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{typeId}");
        var productUpdate = await client.PutAsJsonAsync($"/api/producttypes/{typeId}/products/{type!.Products[0].Id}",
            new { Cavities = 3 });
        productUpdate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync($"/api/molds/{mold!.Id}", new { Cavities = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMold_ReturnsPartAndShotWeightPerProduct()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var type = await db.ProductTypes.Include(t => t.Products).SingleAsync(t => t.Id == typeId);
            type.Products.Single().PartWeightGrams = 10m;
            db.Products.Add(new Product
            {
                ProductTypeId = typeId, Article = "BLK", Name = "Чёрный",
                FullArticle = type.Article + ".BLK", PartWeightGrams = 12.5m
            });
            await db.SaveChangesAsync();
        }
        var request = NewMold(typeId);
        request.Cavities = 2;
        var created = await client.PostAsJsonAsync("/api/molds", request);
        var mold = await created.Content.ReadFromJsonAsync<MoldDto>();

        var response = await client.GetAsync($"/api/molds/{mold!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<MoldDto>();
        dto!.ProductWeights.Should().HaveCount(2);
        dto.ProductWeights.Should().Contain(w => w.PartWeightGrams == 10m && w.ShotWeightGrams == 20m);
        dto.ProductWeights.Should().Contain(w => w.PartWeightGrams == 12.5m && w.ShotWeightGrams == 25m);
    }

    [Fact]
    public async Task GetMold_ShotWeight_UsesProductCavitiesOrMoldMaximum()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var type = await db.ProductTypes.Include(t => t.Products).SingleAsync(t => t.Id == typeId);
            var withDefault = type.Products.Single();
            withDefault.PartWeightGrams = 10m;
            withDefault.Cavities = 2;
            db.Products.Add(new Product
            {
                ProductTypeId = typeId, Article = "MAX", Name = "По максимуму ПФ",
                FullArticle = type.Article + ".MAX", PartWeightGrams = 12.5m
            });
            var mold = new Mold
            {
                Name = "Четырёхгнёздовая", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 4, ProductTypeId = typeId
            };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            var response = await client.GetAsync($"/api/molds/{mold.Id}");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var dto = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var rows = dto.RootElement.GetProperty("productWeights").EnumerateArray().ToList();
            var defaultRow = rows.Single(r => r.GetProperty("fullArticle").GetString() == type.Article + ".0");
            defaultRow.TryGetProperty("cavities", out var defaultCavities).Should()
                .BeTrue("DTO ПФ должен вернуть умолчание гнёзд изделия");
            defaultCavities.GetInt32().Should().Be(2);
            defaultRow.GetProperty("shotWeightGrams").GetDecimal().Should().Be(20m);
            var maximumRow = rows.Single(r => r.GetProperty("fullArticle").GetString() == type.Article + ".MAX");
            maximumRow.GetProperty("cavities").ValueKind.Should().Be(JsonValueKind.Null);
            maximumRow.GetProperty("shotWeightGrams").GetDecimal().Should().Be(50m);
        }
    }

    [Fact]
    public async Task GetMold_ShotWeightOverflow_ReturnsNullInsteadOfError()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var product = await db.Products.SingleAsync(p => p.ProductTypeId == typeId);
            product.PartWeightGrams = decimal.MaxValue;
            var mold = new Mold
            {
                Name = "Переполнение", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 2, ProductTypeId = typeId
            };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var response = await client.GetAsync($"/api/molds/{moldId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("productWeights")[0].GetProperty("shotWeightGrams").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateMold_SetTypeOnLegacyMold_NullToValue_Allowed_EvenWithTasks()
    {
        // Legacy ПФ без типа, но с заданием → присвоение типа разрешено (исключение п.7).
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "Legacy", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = null };
            db.Molds.Add(mold);
            db.ShiftTasks.Add(new Core.Entities.ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = mold.Id, ProductId = _factory.TestProductId, PlanQuantity = 10, Cavities = 1,
                Status = Core.Enums.TaskStatus.Planned, IssuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = typeId });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateMold_ChangeExistingType_WithTasks_Returns409()
    {
        var client = await ManagerClientAsync();
        var type1 = await SeedTypeAsync();
        var type2 = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "M", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = type1 };
            db.Molds.Add(mold);
            db.ShiftTasks.Add(new Core.Entities.ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = mold.Id,
                ProductId = db.Products.Single(p => p.ProductTypeId == type1).Id, PlanQuantity = 10, Cavities = 1,
                Status = Core.Enums.TaskStatus.Completed, IssuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = type2 });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateMold_ChangeExistingType_NoTasks_Allowed()
    {
        var client = await ManagerClientAsync();
        var type1 = await SeedTypeAsync();
        var type2 = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "M", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = type1 };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = type2 });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
