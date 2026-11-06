using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskCavitiesApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public TaskCavitiesApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<(Guid ImmId, Guid MoldId, Guid ProductId, Guid TypeId)> SeedAsync(int moldCavities = 4)
    {
        var immId = await _factory.CreateFreshImmAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var type = new ProductType { Article = $"CAV-{suffix}".ToUpperInvariant(), Name = "Гнёздовой тип" };
        var product = new Product
        {
            ProductTypeId = type.Id, Article = "0", Name = "Деталь", FullArticle = type.Article + ".0"
        };
        var mold = new Mold
        {
            ProductType = type, Name = "Гнёздовая ПФ", FormId = $"CAV-{suffix}", Cavities = moldCavities
        };
        type.Products.Add(product);
        db.ProductTypes.Add(type);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (immId, mold.Id, product.Id, type.Id);
    }

    private static object CreateRequest(Guid immId, Guid moldId, Guid productId, int? cavities = null) => new
    {
        ImmId = immId,
        MoldId = moldId,
        ProductId = productId,
        PlanQuantity = 100,
        Cavities = cavities,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
        PlannedFullCycleSeconds = 60
    };

    [Fact]
    public async Task CreateTask_WithoutProductDefault_UsesMoldCavities()
    {
        var (immId, moldId, productId, _) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var task = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        task.RootElement.TryGetProperty("cavities", out var cavities).Should()
            .BeTrue("задание должно вернуть рабочую гнёздовость");
        cavities.GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task CreateTask_WithProductDefault_UsesProductCavities()
    {
        var (immId, moldId, _, typeId) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();
        var productResponse = await client.PostAsJsonAsync($"/api/producttypes/{typeId}/products", new
        {
            Article = "PAIR", Name = "Парная деталь", Cavities = 2
        });
        productResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        using var product = await JsonDocument.ParseAsync(await productResponse.Content.ReadAsStreamAsync());
        var productId = product.RootElement.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var task = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        task.RootElement.TryGetProperty("cavities", out var cavities).Should()
            .BeTrue("задание должно вернуть рабочую гнёздовость");
        cavities.GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task CreateTask_ManagerCanReduceDefaultCavities()
    {
        var (immId, moldId, productId, _) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId, cavities: 2));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var task = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        task.RootElement.GetProperty("cavities").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task CreateTask_CavitiesOutsideDefaultRange_BadRequest(int cavities)
    {
        var (immId, moldId, productId, _) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId, cavities));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_AllowsCavitiesReductionForClosedTask()
    {
        var (immId, moldId, productId, _) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        using var createdTask = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        var taskId = createdTask.RootElement.GetProperty("id").GetGuid();
        var closed = await client.PostAsJsonAsync($"/api/tasks/{taskId}/close", new { closeReason = "тест" });
        closed.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync($"/api/tasks/{taskId}", new { Cavities = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var updated = await JsonDocument.ParseAsync(await (await client.GetAsync($"/api/tasks/{taskId}"))
            .Content.ReadAsStreamAsync());
        updated.RootElement.GetProperty("cavities").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task UpdateTask_CavitiesOutsideDefaultRange_BadRequest(int cavities)
    {
        var (immId, moldId, productId, _) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        using var createdTask = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        var taskId = createdTask.RootElement.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/tasks/{taskId}", new { Cavities = cavities });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_UnchangedCavitiesAboveNewProductDefault_IsAllowed()
    {
        var (immId, moldId, productId, typeId) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        using var createdTask = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        var taskId = createdTask.RootElement.GetProperty("id").GetGuid();
        var productUpdate = await client.PutAsJsonAsync($"/api/producttypes/{typeId}/products/{productId}",
            new { Cavities = 2 });
        productUpdate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync($"/api/tasks/{taskId}", new
        {
            Cavities = 4, Note = "Значение задания не менялось"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var updated = await JsonDocument.ParseAsync(await (await client.GetAsync($"/api/tasks/{taskId}"))
            .Content.ReadAsStreamAsync());
        updated.RootElement.GetProperty("cavities").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task UpdateTask_ChangeProduct_RefreshesCavitiesDefault()
    {
        var (immId, moldId, productId, typeId) = await SeedAsync(moldCavities: 4);
        var client = await ClientAsync();
        var productResponse = await client.PostAsJsonAsync($"/api/producttypes/{typeId}/products", new
        {
            Article = "PAIR", Name = "Парная деталь", Cavities = 2
        });
        using var product = await JsonDocument.ParseAsync(await productResponse.Content.ReadAsStreamAsync());
        var secondProductId = product.RootElement.GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        using var createdTask = await JsonDocument.ParseAsync(await created.Content.ReadAsStreamAsync());
        var taskId = createdTask.RootElement.GetProperty("id").GetGuid();

        var response = await client.PutAsJsonAsync($"/api/tasks/{taskId}", new { ProductId = secondProductId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var updated = await JsonDocument.ParseAsync(await (await client.GetAsync($"/api/tasks/{taskId}"))
            .Content.ReadAsStreamAsync());
        updated.RootElement.GetProperty("cavities").GetInt32().Should().Be(2);
    }
}
