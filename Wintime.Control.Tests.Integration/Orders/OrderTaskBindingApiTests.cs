using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrderTaskBindingApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public OrderTaskBindingApiTests(IntegrationTestFactory factory) => _factory = factory;

    // Контроллер сериализует enum-статусы строками (JsonStringEnumConverter в Program.cs);
    // ReadFromJsonAsync без опций использует бинарный конвертер enum по умолчанию — падает на "Active".
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

    private async Task<Guid> CreateOrderAsync(HttpClient client) =>
        (await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = "ORD-B", OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = _factory.TestProductTypeId, Quantity = 100
        })).Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!.Id;

    private async Task<Guid> CreateTaskAsync(Guid moldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var t = new ShiftTask { ImmId = _factory.TestImmId, MoldId = moldId, PlanQuantity = 50, Status = TaskStatus.Issued, IssuedAt = DateTime.UtcNow };
        db.ShiftTasks.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task Attach_CompatibleTask_Ok_AndDetach_ClearsOrderId()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client);
        var taskId = await CreateTaskAsync(_factory.TestMoldId);  // ПФ с TestProductType

        (await client.PostAsJsonAsync($"/api/orders/{orderId}/tasks", new AttachTaskRequestDto { TaskId = taskId }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftTasks.FindAsync(taskId))!.OrderId.Should().Be(orderId);
        }

        (await client.DeleteAsync($"/api/orders/{orderId}/tasks/{taskId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.ShiftTasks.FindAsync(taskId))!.OrderId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Attach_ProductTypeMismatch_Returns400()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client);

        // ПФ другого типа изделия
        Guid otherMoldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var otherPt = new ProductType { Article = "OTHER-TYPE", Name = "Other", IsActive = true };
            db.ProductTypes.Add(otherPt);
            var mold = new Mold { Name = "Other Mold", FormId = $"OM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true, ProductTypeId = otherPt.Id };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            otherMoldId = mold.Id;
        }
        var taskId = await CreateTaskAsync(otherMoldId);

        (await client.PostAsJsonAsync($"/api/orders/{orderId}/tasks", new AttachTaskRequestDto { TaskId = taskId }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Attach_ToCancelledOrder_Returns400()
    {
        var client = await ManagerClientAsync();
        var orderId = await CreateOrderAsync(client);
        await client.PostAsync($"/api/orders/{orderId}/cancel", null);
        var taskId = await CreateTaskAsync(_factory.TestMoldId);

        (await client.PostAsJsonAsync($"/api/orders/{orderId}/tasks", new AttachTaskRequestDto { TaskId = taskId }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
