using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrdersCrudTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public OrdersCrudTests(IntegrationTestFactory factory) => _factory = factory;

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

    private CreateOrderRequestDto ValidOrder(int qty = 100) => new()
    {
        Number = "ORD-100",
        OrderDate = DateTime.UtcNow.Date,
        DueDate = DateTime.UtcNow.Date.AddDays(7),
        ProductTypeId = _factory.TestProductTypeId,
        Quantity = qty
    };

    [Fact]
    public async Task Create_ValidOrder_Returns201_AndActive()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", ValidOrder());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await resp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        dto!.Status.Should().Be(Core.Enums.OrderStatus.Active);
        dto.ProductTypeArticle.Should().Be("TYPE-TEST");
    }

    [Fact]
    public async Task Create_NonPositiveQuantity_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/orders", ValidOrder(qty: 0));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_UnknownProductType_Returns400()
    {
        var client = await ManagerClientAsync();
        var body = ValidOrder();
        body.ProductTypeId = Guid.NewGuid();
        var resp = await client.PostAsJsonAsync("/api/orders", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancel_ThenComplete_Returns400()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/orders", ValidOrder()))
            .Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        (await client.PostAsync($"/api/orders/{created!.Id}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/orders/{created.Id}/complete", null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest); // не Active
    }

    [Fact]
    public async Task Reopen_CancelledOrder_MakesActive()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/orders", ValidOrder()))
            .Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        await client.PostAsync($"/api/orders/{created!.Id}/cancel", null);

        var resp = await client.PostAsync($"/api/orders/{created.Id}/reopen", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reloaded = await (await client.GetAsync($"/api/orders/{created.Id}"))
            .Content.ReadFromJsonAsync<OrderDetailsDto>(JsonOptions);
        reloaded!.Status.Should().Be(Core.Enums.OrderStatus.Active);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var client = await ManagerClientAsync();
        await client.PostAsJsonAsync("/api/orders", ValidOrder());
        var resp = await client.GetAsync("/api/orders?status=Active");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<OrderDto>>(JsonOptions);
        list!.Should().OnlyContain(o => o.Status == Core.Enums.OrderStatus.Active);
    }
}
