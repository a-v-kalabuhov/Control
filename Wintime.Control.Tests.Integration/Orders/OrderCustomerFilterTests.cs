using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Order;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Orders;

[Collection("Integration")]
public class OrderCustomerFilterTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public OrderCustomerFilterTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Order> SeedOrderAsync(
        string marker, string? customer, string? typeCompany,
        OrderStatus status = OrderStatus.Active, Guid? productId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        Product? product = null;
        if (productId == null)
        {
            var article = $"OF-{marker}-{Guid.NewGuid():N}".ToUpperInvariant();
            var type = new ProductType
            {
                Article = article, Name = $"Тип {marker}", Company = typeCompany, IsActive = true
            };
            product = new Product
            {
                Article = "0", Name = "По умолчанию", FullArticle = article + ".0",
                IsActive = true, ProductType = type
            };
        }
        var order = new Order
        {
            Number = $"ORD-{marker}-{Guid.NewGuid():N}", Customer = customer,
            DueDate = DateTime.UtcNow.Date.AddDays(7), Product = product,
            ProductId = productId ?? Guid.Empty, Quantity = 100, Status = status
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task List_FiltersByOrderCustomer_WithoutProductTypeFallback()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var ownCustomer = await SeedOrderAsync(marker, customer, $"Другая компания {marker}");
        var fallbackCandidate = await SeedOrderAsync(marker, null, customer);
        var other = await SeedOrderAsync(marker, $"Другой {marker}", null);

        var namedList = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?customer={Uri.EscapeDataString(customer)}", JsonOptions);
        namedList!.Should().ContainSingle(o => o.Id == ownCustomer.Id);
        namedList.Should().NotContain(o => o.Id == fallbackCandidate.Id || o.Id == other.Id);
    }

    [Fact]
    public async Task List_FiltersUnspecifiedCustomer()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var ownCustomer = await SeedOrderAsync(marker, customer, $"Другая компания {marker}");
        var fallbackCandidate = await SeedOrderAsync(marker, null, customer);
        var other = await SeedOrderAsync(marker, $"Другой {marker}", null);

        var emptyList = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?search={marker}&customerEmpty=true", JsonOptions);
        emptyList!.Should().ContainSingle(o => o.Id == fallbackCandidate.Id);
        emptyList.Should().NotContain(o => o.Id == ownCustomer.Id || o.Id == other.Id);
    }

    [Fact]
    public async Task List_CustomerFilterCombinesWithExistingFilters()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var active = await SeedOrderAsync(marker, customer, null);
        var completed = await SeedOrderAsync(marker, customer, null, OrderStatus.Completed);
        var otherCompleted = await SeedOrderAsync(
            marker, $"Другой {marker}", null, OrderStatus.Completed, completed.ProductId);

        var list = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?status=Completed&search={marker}" +
            $"&productTypeId={completed.Product!.ProductTypeId}&productId={completed.ProductId}" +
            $"&customer={Uri.EscapeDataString(customer)}", JsonOptions);

        list!.Should().ContainSingle(o => o.Id == completed.Id);
        list.Should().NotContain(o => o.Id == active.Id || o.Id == otherCompleted.Id);
    }

    [Fact]
    public async Task List_UnknownCustomer_ReturnsEmpty()
    {
        var client = await ManagerClientAsync();
        var unknown = $"Несуществующий {Guid.NewGuid():N}";

        var list = await client.GetFromJsonAsync<List<OrderDto>>(
            $"/api/orders?customer={Uri.EscapeDataString(unknown)}", JsonOptions);

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_CustomerAndCustomerEmpty_ReturnsBadRequest()
    {
        var client = await ManagerClientAsync();

        var response = await client.GetAsync(
            "/api/orders?customer=ООО%20Ромашка&customerEmpty=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
