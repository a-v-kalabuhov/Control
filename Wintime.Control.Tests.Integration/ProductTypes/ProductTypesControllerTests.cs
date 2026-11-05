using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductTypesControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ProductTypesControllerTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task Manager_CanCreateAndGet()
    {
        var client = await ManagerClientAsync();
        var article = $"ART-{Guid.NewGuid():N}";

        var create = await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "Крышка" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await create.Content.ReadFromJsonAsync<ProductTypeDto>();
        dto!.Article.Should().Be(article.ToUpperInvariant());   // PZP-19: артикул нормализуется
        dto.IsActive.Should().BeTrue();

        var get = await client.GetAsync($"/api/producttypes/{dto.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Company_IsTrimmed_AndEmptyUpdateClearsIt()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
                new CreateProductTypeRequestDto
                {
                    Article = $"CUSTOMER-{Guid.NewGuid():N}", Name = "Крышка", Company = "  ООО Заказчик  "
                }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();
        created!.Company.Should().Be("ООО Заказчик");

        (await client.PutAsJsonAsync($"/api/producttypes/{created.Id}",
                new UpdateProductTypeRequestDto { Company = "" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var cleared = await client.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{created.Id}");
        cleared!.Company.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithoutCompany_KeepsExistingValue()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
                new CreateProductTypeRequestDto
                {
                    Article = $"CUSTOMER-{Guid.NewGuid():N}", Name = "Крышка", Company = "ООО Заказчик"
                }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();

        await client.PutAsJsonAsync($"/api/producttypes/{created!.Id}",
            new UpdateProductTypeRequestDto { Name = "Новое имя" });

        var reloaded = await client.GetFromJsonAsync<ProductTypeDto>($"/api/producttypes/{created.Id}");
        reloaded!.Company.Should().Be("ООО Заказчик");
    }

    [Fact]
    public async Task Create_DuplicateArticle_ReturnsConflict()
    {
        var client = await ManagerClientAsync();
        var article = $"DUP-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "A" });

        var second = await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"  {article.ToUpper()}  ", Name = "B" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_DuplicateArticle_ReturnsConflict_ExcludingSelf()
    {
        var client = await ManagerClientAsync();
        var a1 = $"A1-{Guid.NewGuid():N}";
        var a2 = $"A2-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/producttypes", new CreateProductTypeRequestDto { Article = a1, Name = "X" });
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = a2, Name = "Y" })).Content.ReadFromJsonAsync<ProductTypeDto>();

        // Переименовать второй в артикул первого → конфликт
        var conflict = await client.PutAsJsonAsync($"/api/producttypes/{created!.Id}",
            new UpdateProductTypeRequestDto { Article = a1 });
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Тот же артикул у себя → успех (контроллер возвращает 204 NoContent)
        var ok = await client.PutAsJsonAsync($"/api/producttypes/{created.Id}",
            new UpdateProductTypeRequestDto { Article = a2, Name = "Y2" });
        ok.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Archive_WithLinkedMold_Succeeds_AndKeepsLink()
    {
        var client = await ManagerClientAsync();
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"ARCH-{Guid.NewGuid():N}", Name = "Z" }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();

        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 1,
                                  IsActive = true, ProductTypeId = created!.Id };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var archive = await client.PutAsJsonAsync($"/api/producttypes/{created!.Id}",
            new UpdateProductTypeRequestDto { IsActive = false });
        archive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var verify = _factory.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold2 = await vdb.Molds.FindAsync(moldId);
        mold2!.ProductTypeId.Should().Be(created.Id); // ссылка сохранена
    }

    [Fact]
    public async Task List_FiltersByIsActive()
    {
        var client = await ManagerClientAsync();
        var active = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"ACT-{Guid.NewGuid():N}", Name = "Act" }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();
        var archived = await (await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"ARV-{Guid.NewGuid():N}", Name = "Arv" }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();
        await client.PutAsJsonAsync($"/api/producttypes/{archived!.Id}",
            new UpdateProductTypeRequestDto { IsActive = false });

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>("/api/producttypes?isActive=true");
        list!.Should().Contain(p => p.Id == active!.Id);
        list.Should().NotContain(p => p.Id == archived.Id);
    }

    private async Task CreateOrderWithCustomerAsync(HttpClient client, string customer)
    {
        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            Number = $"ORD-{Guid.NewGuid():N}",
            DueDate = DateTime.UtcNow.Date.AddDays(7),
            ProductId = _factory.TestProductId,
            Quantity = 100,
            Customer = customer
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Suggestions_Companies_UnionTypesAndOrders_DistinctIncludingArchived()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var shared = $"Общий заказчик {marker}";
        var orderOnly = $"Заказчик из заказа {marker}";
        var created = await (await client.PostAsJsonAsync("/api/producttypes",
                new CreateProductTypeRequestDto
                {
                    Article = $"SUG-{Guid.NewGuid():N}", Name = "Тип", Company = shared
                }))
            .Content.ReadFromJsonAsync<ProductTypeDto>();
        await client.PutAsJsonAsync($"/api/producttypes/{created!.Id}",
            new UpdateProductTypeRequestDto { IsActive = false });
        await CreateOrderWithCustomerAsync(client, shared);
        await CreateOrderWithCustomerAsync(client, orderOnly);

        var list = await client.GetFromJsonAsync<List<string>>(
            $"/api/producttypes/suggestions/companies?search={marker.ToUpperInvariant()}");

        list.Should().BeEquivalentTo(new[] { shared, orderOnly });
    }

    [Fact]
    public async Task Suggestions_Companies_AreLimitedTo20()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        for (var i = 0; i < 22; i++)
        {
            var response = await client.PostAsJsonAsync("/api/producttypes",
                new CreateProductTypeRequestDto
                {
                    Article = $"SUG-{marker}-{i}", Name = $"Тип {i}", Company = $"Лимит {marker} {i:D2}"
                });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var list = await client.GetFromJsonAsync<List<string>>(
            $"/api/producttypes/suggestions/companies?search={marker}");

        list.Should().HaveCount(20);
    }

    [Fact]
    public async Task Adjuster_CannotCreate_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = $"F-{Guid.NewGuid():N}", Name = "N" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Adjuster_CanRead_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.GetAsync("/api/producttypes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
