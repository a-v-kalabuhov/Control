using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskProductGuardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskProductGuardTests(IntegrationTestFactory factory) => _factory = factory;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task CreateTask_ForMoldWithoutType_Returns400()
    {
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "NoType", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = null };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, productId = _factory.TestProductId, planQuantity = 10, note = "x", plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            plannedFullCycleSeconds = 30
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_ForMoldWithType_Returns201()
    {
        // TestMoldId сидируется с TestProductTypeId → задание создаётся.
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId = _factory.TestMoldId, productId = _factory.TestProductId, planQuantity = 10, note = "x", plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            plannedFullCycleSeconds = 30
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> PostTaskAsync(HttpClient client, Guid? productId, Guid? moldId = null, Guid? orderId = null)
        => await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId = moldId ?? _factory.TestMoldId, productId, orderId, planQuantity = 10, note = "x",
            plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), plannedFullCycleSeconds = 30
        });

    private async Task<Guid> CreateTaskAsync(HttpClient client, Guid? orderId = null)
    {
        var resp = await PostTaskAsync(client, _factory.TestProductId, orderId: orderId);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<TaskDto>(JsonOpts))!.Id;
    }

    /// <summary>Второе изделие тестового типа через API; archived — сразу архивируется.</summary>
    private async Task<Guid> AddProductAsync(HttpClient client, bool archived = false)
    {
        var part = Guid.NewGuid().ToString("N")[..8];
        var resp = await client.PostAsJsonAsync($"/api/producttypes/{_factory.TestProductTypeId}/products",
            new { article = part, name = "Второе " + part });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await resp.Content.ReadFromJsonAsync<Wintime.Control.Core.DTOs.ProductType.ProductDto>())!.Id;
        if (archived)
        {
            var put = await client.PutAsJsonAsync($"/api/producttypes/{_factory.TestProductTypeId}/products/{id}", new { isActive = false });
            put.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }
        return id;
    }

    [Fact]
    public async Task Create_WithoutProduct_BadRequest()
    {
        var client = await ManagerClientAsync();
        (await PostTaskAsync(client, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_ProductOfOtherType_BadRequest()
    {
        var client = await ManagerClientAsync();
        var (_, otherProductId) = await _factory.CreateProductTypeAsync();
        var resp = await PostTaskAsync(client, otherProductId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Изделие не относится к типу изделия пресс-формы");
    }

    [Fact]
    public async Task Create_ArchivedProduct_BadRequest()
    {
        var client = await ManagerClientAsync();
        var archived = await AddProductAsync(client, archived: true);
        var resp = await PostTaskAsync(client, archived);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Изделие в архиве");
    }

    [Fact]
    public async Task Create_ReturnsProductInDto()
    {
        var client = await ManagerClientAsync();
        var resp = await PostTaskAsync(client, _factory.TestProductId);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<TaskDto>(JsonOpts);
        dto!.ProductId.Should().Be(_factory.TestProductId);
        dto.ProductFullArticle.Should().Be("TYPE-TEST.0");
    }

    [Fact]
    public async Task Update_ChangeProduct_Planned_Ok()
    {
        var client = await ManagerClientAsync();
        var id = await CreateTaskAsync(client);
        var second = await AddProductAsync(client);

        (await client.PutAsJsonAsync($"/api/tasks/{id}", new { productId = second })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        var dto = await (await client.GetAsync($"/api/tasks/{id}")).Content.ReadFromJsonAsync<TaskDto>(JsonOpts);
        dto!.ProductId.Should().Be(second);
    }

    [Fact]
    public async Task Update_ChangeProduct_Issued_BadRequest()
    {
        var client = await ManagerClientAsync();
        var id = await CreateTaskAsync(client);
        var second = await AddProductAsync(client);
        (await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { personnelId = _factory.TestAdjusterId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync($"/api/tasks/{id}", new { productId = second })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ChangeProduct_WithOrder_BadRequest()
    {
        var client = await ManagerClientAsync();
        var orderResp = await client.PostAsJsonAsync("/api/orders", new
        {
            Number = $"ORD-{Guid.NewGuid():N}"[..12], DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductId = _factory.TestProductId, Quantity = 100
        });
        orderResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = (await orderResp.Content.ReadFromJsonAsync<Wintime.Control.Core.DTOs.Order.OrderDto>(
            JsonOpts))!.Id;
        var id = await CreateTaskAsync(client, orderId);
        var second = await AddProductAsync(client);

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { productId = second });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Изделие задания с заказом задаёт заказ");
    }

    [Fact]
    public async Task Update_ProductOfOtherType_BadRequest()
    {
        var client = await ManagerClientAsync();
        var id = await CreateTaskAsync(client);
        var (_, other) = await _factory.CreateProductTypeAsync();
        (await client.PutAsJsonAsync($"/api/tasks/{id}", new { productId = other })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_SameProduct_InIssued_Ok()
    {
        var client = await ManagerClientAsync();
        var id = await CreateTaskAsync(client);
        (await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { personnelId = _factory.TestAdjusterId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PutAsJsonAsync($"/api/tasks/{id}", new { productId = _factory.TestProductId })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Search_ByProductFullArticle()
    {
        var client = await ManagerClientAsync();
        var id = await CreateTaskAsync(client);

        var list = await client.GetFromJsonAsync<List<TaskDto>>("/api/tasks?search=TYPE-TEST.0", JsonOpts);
        list!.Select(t => t.Id).Should().Contain(id);

        var none = await client.GetFromJsonAsync<List<TaskDto>>("/api/tasks?search=NO-SUCH-ARTICLE-XYZ", JsonOpts);
        none!.Select(t => t.Id).Should().NotContain(id);
    }
}
