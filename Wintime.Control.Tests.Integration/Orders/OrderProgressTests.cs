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
public class OrderProgressTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public OrderProgressTests(IntegrationTestFactory factory) => _factory = factory;

    // Контроллер сериализует enum-статусы строками (JsonStringEnumConverter в Program.cs);
    // ReadFromJsonAsync без опций использует бинарный конвертер enum по умолчанию — падает на "Active".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Progress_CountsGoodOnly_AndNotEnough_BlocksComplete()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var order = await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = "ORD-P", OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = _factory.TestProductTypeId, Quantity = 100
        })).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        // Два задания того же заказа: 60 годных + 50 выпущено − 10 брак = 40 годных → всего 100 годных
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftTasks.Add(new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
                PlanQuantity = 60, ActualQuantity = 60, DefectQuantity = 0, Status = TaskStatus.Completed, OrderId = order!.Id });
            db.ShiftTasks.Add(new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
                PlanQuantity = 50, ActualQuantity = 50, DefectQuantity = 10, Status = TaskStatus.Completed, OrderId = order.Id });
            await db.SaveChangesAsync();
        }

        var details = await (await client.GetAsync($"/api/orders/{order!.Id}"))
            .Content.ReadFromJsonAsync<OrderDetailsDto>(JsonOptions);
        details!.ProducedQuantity.Should().Be(110);
        details.DefectQuantity.Should().Be(10);
        details.GoodQuantity.Should().Be(100);   // 60 + (50 − 10)
        details.ProgressPercent.Should().Be(100);
        details.Tasks.Should().HaveCount(2);

        // Годных ровно 100 = Quantity → завершение проходит
        (await client.PostAsync($"/api/orders/{order.Id}/complete", null))
            .IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Complete_WhenGoodBelowQuantity_Returns400()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var order = await (await client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            Number = "ORD-P2", OrderDate = DateTime.UtcNow.Date, DueDate = DateTime.UtcNow.Date.AddDays(3),
            ProductTypeId = _factory.TestProductTypeId, Quantity = 100
        })).Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.ShiftTasks.Add(new ShiftTask { ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId,
                PlanQuantity = 100, ActualQuantity = 100, DefectQuantity = 5, Status = TaskStatus.Completed, OrderId = order!.Id });
            await db.SaveChangesAsync();
        }

        (await client.PostAsync($"/api/orders/{order!.Id}/complete", null))
            .StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest); // 95 годных < 100
    }
}
