using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskPackagingUnitApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public TaskPackagingUnitApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<(Guid ImmId, Guid MoldId, Guid ProductId)> SeedAsync(int? packagingUnit)
    {
        var immId = await _factory.CreateFreshImmAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var type = new ProductType
        {
            Article = $"PKG-{suffix}".ToUpperInvariant(), Name = "Тарный тип", PackagingUnit = packagingUnit
        };
        var product = new Product
        {
            ProductTypeId = type.Id, Article = "0", Name = "Деталь", FullArticle = type.Article + ".0"
        };
        var mold = new Mold
        {
            ProductType = type, Name = "Тарная ПФ", FormId = $"PKG-F-{suffix}", Cavities = 1
        };
        type.Products.Add(product);
        db.ProductTypes.Add(type);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (immId, mold.Id, product.Id);
    }

    private static CreateTaskRequestDto CreateRequest(Guid immId, Guid moldId, Guid productId) => new()
    {
        ImmId = immId, MoldId = moldId, ProductId = productId, PlanQuantity = 100,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), PlannedFullCycleSeconds = 60
    };

    [Fact]
    public async Task CreateTask_CopiesProductTypePackagingUnit()
    {
        var (immId, moldId, productId) = await SeedAsync(24);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        task!.PackagingUnit.Should().Be(24);
    }

    [Fact]
    public async Task CreateTask_ExplicitPackagingUnitOverridesTypeDefault()
    {
        var (immId, moldId, productId) = await SeedAsync(24);
        var client = await ClientAsync();
        var request = CreateRequest(immId, moldId, productId);
        request.PackagingUnit = 48;

        var response = await client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        task!.PackagingUnit.Should().Be(48);
    }

    [Fact]
    public async Task CreateTask_ClearPackagingUnit_DoesNotCopyTypeDefault()
    {
        var (immId, moldId, productId) = await SeedAsync(24);
        var client = await ClientAsync();
        var request = CreateRequest(immId, moldId, productId);
        request.ClearPackagingUnit = true;

        var response = await client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        task!.PackagingUnit.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTask_ChangesPackagingUnitForClosedTask()
    {
        var (immId, moldId, productId) = await SeedAsync(24);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        await client.PostAsJsonAsync($"/api/tasks/{task!.Id}/close", new { closeReason = "тест" });

        var response = await client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequestDto { PackagingUnit = 36 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions);
        updated!.Status.Should().Be(TaskStatus.Closed);
        updated.PackagingUnit.Should().Be(36);
    }

    [Fact]
    public async Task UpdateTask_ClearsPackagingUnit()
    {
        var (immId, moldId, productId) = await SeedAsync(24);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);

        var response = await client.PutAsJsonAsync($"/api/tasks/{task!.Id}",
            new UpdateTaskRequestDto { ClearPackagingUnit = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions);
        updated!.PackagingUnit.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTask_TypeDefaultChange_DoesNotChangeExistingTask()
    {
        var (immId, moldId, productId) = await SeedAsync(24);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        Guid typeId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var stored = await db.ShiftTasks.Include(t => t.Product).SingleAsync(t => t.Id == task!.Id);
            typeId = stored.Product!.ProductTypeId;
        }

        var typeResponse = await client.PutAsJsonAsync($"/api/producttypes/{typeId}",
            new Wintime.Control.Core.DTOs.ProductType.UpdateProductTypeRequestDto { PackagingUnit = 48 });
        typeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unchanged = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task!.Id}", JsonOptions);
        unchanged!.PackagingUnit.Should().Be(24);
    }
}
