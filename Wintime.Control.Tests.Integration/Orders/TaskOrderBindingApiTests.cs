using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Orders;

/// <summary>
/// PZP-05 (I2): интеграционные тесты для привязки заказа со стороны задания —
/// POST /api/tasks/{id}/set-order и OrderId в POST /api/tasks (CreateTask).
/// Отдельно от OrderTaskBindingApiTests, который покрывает привязку/отвязку со стороны заказа
/// (POST/DELETE /api/orders/{id}/tasks).
/// </summary>
[Collection("Integration")]
public class TaskOrderBindingApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskOrderBindingApiTests(IntegrationTestFactory factory) => _factory = factory;

    // Контроллер сериализует enum-статусы строками (JsonStringEnumConverter в Program.cs);
    // ReadFromJsonAsync без опций использует бинарный конвертер enum по умолчанию — падает на "Active"/"Issued".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> CreateOrderAsync(HttpClient client, Guid productTypeId, string number = "ORD-TB")
    {
        var response = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = number, OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = productTypeId, Quantity = 100
        });
        var dto = await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        return dto!.Id;
    }

    private async Task<Guid> CreateIssuedTaskAsync(Guid moldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var t = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = moldId, PlanQuantity = 50,
            Status = TaskStatus.Issued, IssuedAt = DateTime.UtcNow
        };
        db.ShiftTasks.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task<Guid> CreateOtherProductTypeMoldAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var otherPt = new ProductType { Article = $"OTHER-{Guid.NewGuid():N}"[..16], Name = "Other", IsActive = true };
        db.ProductTypes.Add(otherPt);
        var mold = new Mold
        {
            Name = "Other Mold (task-order-binding)", FormId = $"OM-{Guid.NewGuid():N}",
            Cavities = 1, IsActive = true, ProductTypeId = otherPt.Id
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    [Fact]
    public async Task SetOrder_HappyPath_Returns200_AndPersistsOrderId()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client, _factory.TestProductTypeId, "ORD-SO-OK");
        var taskId = await CreateIssuedTaskAsync(_factory.TestMoldId);

        var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/set-order", new SetOrderRequestDto { OrderId = orderId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftTasks.FindAsync(taskId))!.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task SetOrder_ProductTypeMismatch_Returns400()
    {
        var client = await ManagerClientAsync();
        var otherMoldId = await CreateOtherProductTypeMoldAsync();
        var orderId = await CreateOrderAsync(client, _factory.TestProductTypeId, "ORD-SO-MISMATCH");
        var taskId = await CreateIssuedTaskAsync(otherMoldId);

        var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/set-order", new SetOrderRequestDto { OrderId = orderId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftTasks.FindAsync(taskId))!.OrderId.Should().BeNull();
    }

    [Fact]
    public async Task CreateTask_WithMatchingOrderId_Returns201_AndSetsOrderId()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client, _factory.TestProductTypeId, "ORD-CT-OK");

        var response = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequestDto
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 10,
            OrderId = orderId, PlannedFullCycleSeconds = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TaskDto>(JsonOptions);
        created!.OrderId.Should().Be(orderId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.ShiftTasks.FindAsync(created.Id))!.OrderId.Should().Be(orderId);
    }

    [Fact]
    public async Task CreateTask_WithMismatchedOrderId_Returns400()
    {
        var client = await ManagerClientAsync();
        var otherMoldId = await CreateOtherProductTypeMoldAsync();
        var orderId = await CreateOrderAsync(client, _factory.TestProductTypeId, "ORD-CT-MISMATCH");

        var response = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequestDto
        {
            ImmId = _factory.TestImmId, MoldId = otherMoldId, PlanQuantity = 10,
            OrderId = orderId, PlannedFullCycleSeconds = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_WithNonExistentOrderId_Returns400()
    {
        var client = await ManagerClientAsync();

        var response = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequestDto
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 10,
            OrderId = Guid.NewGuid(), PlannedFullCycleSeconds = 30
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
