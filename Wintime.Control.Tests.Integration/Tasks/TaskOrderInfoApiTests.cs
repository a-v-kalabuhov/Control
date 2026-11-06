using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

/// <summary>Задание отдаёт номер и дату своего заказа — для карточки задания.</summary>
[Collection("Integration")]
public class TaskOrderInfoApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskOrderInfoApiTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetTask_ReturnsOrderNumberAndDate()
    {
        var orderDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var productType = db.ProductTypes.Single(t => t.Products.Any(p => p.Id == _factory.TestProductId));
            productType.Company = "Компания типа";
            var order = new Order
            {
                Number = "ORD-77", Customer = "Заказчик заказа", OrderDate = orderDate, DueDate = orderDate.AddDays(30),
                ProductId = _factory.TestProductId, Quantity = 100
            };
            db.Orders.Add(order);
            var task = new ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, ProductId = _factory.TestProductId, PlanQuantity = 10, Cavities = 1,
                Status = TaskStatus.Planned, OrderId = order.Id
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var body = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{taskId}");

        body.GetProperty("orderNumber").GetString().Should().Be("ORD-77");
        body.GetProperty("orderDate").GetDateTime().ToUniversalTime().Should().Be(orderDate);
        body.GetProperty("orderCustomer").GetString().Should().Be("Заказчик заказа");
        body.GetProperty("productTypeCompany").GetString().Should().Be("Компания типа");
    }
}
