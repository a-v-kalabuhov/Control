using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskPartWeightApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public TaskPartWeightApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<(Guid ImmId, Guid MoldId, Guid ProductId, Guid TypeId)> SeedAsync(decimal weight)
    {
        var immId = await _factory.CreateFreshImmAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var type = new ProductType { Article = $"W-{suffix}".ToUpperInvariant(), Name = "Весовой тип" };
        var product = new Product
        {
            ProductTypeId = type.Id, Article = "0", Name = "Деталь", FullArticle = type.Article + ".0",
            PartWeightGrams = weight
        };
        var mold = new Mold
        {
            ProductType = type, Name = "Весовая ПФ", FormId = $"WF-{suffix}", Cavities = 2,
            RunnerWeightGrams = 3
        };
        type.Products.Add(product);
        db.ProductTypes.Add(type);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (immId, mold.Id, product.Id, type.Id);
    }

    private static CreateTaskRequestDto CreateRequest(Guid immId, Guid moldId, Guid productId) => new()
    {
        ImmId = immId, MoldId = moldId, ProductId = productId, PlanQuantity = 100,
        PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), PlannedFullCycleSeconds = 60
    };

    [Fact]
    public async Task CreateTask_CopiesProductPartWeight()
    {
        var (immId, moldId, productId, _) = await SeedAsync(12.5m);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        task!.PartWeightGrams.Should().Be(12.5m);
    }

    [Fact]
    public async Task CreateTask_ExplicitPartWeightOverridesProductDefault()
    {
        var (immId, moldId, productId, _) = await SeedAsync(12.5m);
        var client = await ClientAsync();
        var request = CreateRequest(immId, moldId, productId);
        request.PartWeightGrams = 14m;

        var response = await client.PostAsJsonAsync("/api/tasks", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var task = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        task!.PartWeightGrams.Should().Be(14m);
    }

    [Fact]
    public async Task ChangeProduct_RefreshesTaskPartWeightSnapshot()
    {
        var (immId, moldId, productId, typeId) = await SeedAsync(12.5m);
        Guid secondProductId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var second = new Product
            {
                ProductTypeId = typeId, Article = "BLK", Name = "Чёрная деталь",
                FullArticle = $"SECOND-{Guid.NewGuid():N}", PartWeightGrams = 30m
            };
            db.Products.Add(second);
            await db.SaveChangesAsync();
            secondProductId = second.Id;
        }
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);

        var response = await client.PutAsJsonAsync($"/api/tasks/{task!.Id}",
            new UpdateTaskRequestDto { ProductId = secondProductId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions);
        updated!.PartWeightGrams.Should().Be(30m);
    }

    [Fact]
    public async Task UpdateTask_ClearsInjectionCycle()
    {
        var (immId, moldId, productId, _) = await SeedAsync(12.5m);
        var client = await ClientAsync();
        var request = CreateRequest(immId, moldId, productId);
        request.PlannedInjectionCycleSeconds = 25;
        var created = await client.PostAsJsonAsync("/api/tasks", request);
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);

        var response = await client.PutAsJsonAsync($"/api/tasks/{task!.Id}",
            new UpdateTaskRequestDto { ClearPlannedInjectionCycleSeconds = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions);
        updated!.PlannedInjectionCycleSeconds.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTask_RecalculatesAccumulatedMaterialFromCycles()
    {
        var (immId, moldId, productId, _) = await SeedAsync(10m);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            for (var i = 0; i < 2; i++)
            {
                db.ImmCycles.Add(new ImmCycle
                {
                    ImmId = immId, MoldId = moldId, TaskId = task!.Id, Cavities = 2, IsSuccessful = true,
                    StartTime = DateTime.UtcNow.AddMinutes(-i - 2), EndTime = DateTime.UtcNow.AddMinutes(-i - 1),
                    DurationSeconds = 60
                });
            }
            var stored = await db.ShiftTasks.FindAsync(task!.Id);
            stored!.ActualMaterialWeightGrams = 46; // 2 × (2 детали × 10 г + литник 3 г)
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync($"/api/tasks/{task!.Id}",
            new UpdateTaskRequestDto { PartWeightGrams = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions);
        updated!.ActualMaterialWeightGrams.Should().Be(86); // 2 × (2 детали × 20 г + литник 3 г)
    }

    [Fact]
    public async Task UpdateTask_AllowsPartWeightChangeForClosedTask()
    {
        var (immId, moldId, productId, _) = await SeedAsync(12.5m);
        var client = await ClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateRequest(immId, moldId, productId));
        var task = await created.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        var closed = await client.PostAsJsonAsync($"/api/tasks/{task!.Id}/close", new { closeReason = "тест" });
        closed.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PutAsJsonAsync($"/api/tasks/{task.Id}",
            new UpdateTaskRequestDto { PartWeightGrams = 18.5m });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var updated = await client.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions);
        updated!.Status.Should().Be(TaskStatus.Closed);
        updated.PartWeightGrams.Should().Be(18.5m);
    }
}
