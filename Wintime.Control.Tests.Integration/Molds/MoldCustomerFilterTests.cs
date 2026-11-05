using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldCustomerFilterTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldCustomerFilterTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Mold> SeedMoldAsync(
        string marker, string? company, bool withType = true, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        ProductType? type = null;
        if (withType)
        {
            var article = $"MF-{marker}-{Guid.NewGuid():N}".ToUpperInvariant();
            type = new ProductType
            {
                Article = article, Name = $"Тип {marker}", Company = company, IsActive = true
            };
            type.Products.Add(new Product
            {
                Article = "0", Name = "По умолчанию", FullArticle = article + ".0", IsActive = true
            });
        }
        var mold = new Mold
        {
            FormId = $"PF-{Guid.NewGuid():N}", Name = $"ПФ {marker}", Cavities = 1,
            IsActive = isActive, ProductType = type
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold;
    }

    private async Task SeedOrderWithCustomerAsync(string customer)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.Orders.Add(new Order
        {
            Number = $"ORD-{Guid.NewGuid():N}", Customer = customer,
            DueDate = DateTime.UtcNow.Date.AddDays(7),
            ProductId = _factory.TestProductId, Quantity = 1
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task List_FiltersByProductTypeCompany()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var match = await SeedMoldAsync(marker, customer);
        var other = await SeedMoldAsync(marker, $"Другой {marker}");
        var differentCase = await SeedMoldAsync(marker, customer.ToUpperInvariant());
        var withoutType = await SeedMoldAsync(marker, null, withType: false);

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?customer={Uri.EscapeDataString(customer)}");

        list!.Should().ContainSingle(m => m.Id == match.Id);
        list.Should().NotContain(m => m.Id == other.Id || m.Id == differentCase.Id || m.Id == withoutType.Id);
    }

    [Fact]
    public async Task List_FiltersUnspecifiedCustomer()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var noCompany = await SeedMoldAsync(marker, null);
        var noType = await SeedMoldAsync(marker, null, withType: false);
        var named = await SeedMoldAsync(marker, $"Заказчик {marker}");

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?search={marker}&customerEmpty=true");

        list!.Should().Contain(m => m.Id == noCompany.Id);
        list.Should().Contain(m => m.Id == noType.Id);
        list.Should().NotContain(m => m.Id == named.Id);
    }

    [Fact]
    public async Task List_OrderOnlyCustomer_ReturnsEmpty()
    {
        var client = await ManagerClientAsync();
        var orderOnly = $"Только заказ {Guid.NewGuid():N}";
        await SeedOrderWithCustomerAsync(orderOnly);

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?customer={Uri.EscapeDataString(orderOnly)}");

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_CustomerFilterCombinesWithExistingFilters()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var active = await SeedMoldAsync(marker, customer, isActive: true);
        var archived = await SeedMoldAsync(marker, customer, isActive: false);
        var archivedOther = await SeedMoldAsync(marker, $"Другой {marker}", isActive: false);

        var list = await client.GetFromJsonAsync<List<MoldDto>>(
            $"/api/molds?isActive=false&search={marker}&customer={Uri.EscapeDataString(customer)}");

        list!.Should().ContainSingle(m => m.Id == archived.Id);
        list.Should().NotContain(m => m.Id == active.Id || m.Id == archivedOther.Id);
    }

    [Fact]
    public async Task List_CustomerAndCustomerEmpty_ReturnsBadRequest()
    {
        var client = await ManagerClientAsync();

        var response = await client.GetAsync(
            "/api/molds?customer=ООО%20Ромашка&customerEmpty=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            "Параметры customer и customerEmpty нельзя передавать вместе.");
    }
}
