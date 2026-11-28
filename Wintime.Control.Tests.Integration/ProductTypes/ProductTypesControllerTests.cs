using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.ProductTypes;

[Collection("Integration")]
public class ProductTypesControllerTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public ProductTypesControllerTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<ProductType> SeedCustomerTypeAsync(
        string marker, string? company, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var article = $"CF-{marker}-{Guid.NewGuid():N}".ToUpperInvariant();
        var type = new ProductType
        {
            Article = article, Name = $"Тип {marker}", Company = company, IsActive = isActive
        };
        type.Products.Add(new Product
        {
            Article = "0", Name = "По умолчанию", FullArticle = article + ".0", IsActive = true
        });
        db.ProductTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    private async Task<Material> SeedMaterialAsync(MaterialType type, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var id = Guid.NewGuid();
        var material = new Material
        {
            Type = type,
            Article = $"{type}-{id:N}".ToUpperInvariant(),
            Name = $"Материал {id:N}",
            ColorName = type == MaterialType.Colorant ? "Тестовый" : null,
            IsActive = isActive
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        return material;
    }

    private async Task<ProductType> SeedRecipeTypeAsync(
        string marker,
        string suffix,
        params (string Part, bool IsActive, Material[] Materials)[] products)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var article = $"RF-{marker}-{suffix}-{Guid.NewGuid():N}".ToUpperInvariant();
        var type = new ProductType { Article = article, Name = $"Тип {marker} {suffix}", IsActive = true };
        foreach (var productSeed in products)
        {
            var product = new Product
            {
                Article = productSeed.Part,
                Name = $"Изделие {productSeed.Part}",
                FullArticle = $"{article}.{productSeed.Part}",
                IsActive = productSeed.IsActive
            };
            for (var index = 0; index < productSeed.Materials.Length; index++)
            {
                var material = productSeed.Materials[index];
                product.RecipeIngredients.Add(new ProductRecipeIngredient
                {
                    MaterialId = material.Id,
                    Percent = material.Type == MaterialType.Plastic ? 100 : 1,
                    SortOrder = index
                });
            }
            type.Products.Add(product);
        }
        db.ProductTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    [Fact]
    public async Task List_PlasticFilter_MatchesArchivedMaterialInArchivedNonDefaultProduct()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var selected = await SeedMaterialAsync(MaterialType.Plastic, isActive: false);
        var other = await SeedMaterialAsync(MaterialType.Plastic);
        var match = await SeedRecipeTypeAsync(marker, "match",
            ("0", true, Array.Empty<Material>()),
            ("OLD", false, new[] { selected }));
        var nonMatch = await SeedRecipeTypeAsync(marker, "other",
            ("0", true, new[] { other }));

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&plasticId={selected.Id}", JsonOptions);

        list!.Should().ContainSingle(t => t.Id == match.Id);
        list.Should().NotContain(t => t.Id == nonMatch.Id);
    }

    [Fact]
    public async Task List_ColorantFilter_MatchesNonDefaultProductRecipe()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var selected = await SeedMaterialAsync(MaterialType.Colorant);
        var other = await SeedMaterialAsync(MaterialType.Colorant);
        var match = await SeedRecipeTypeAsync(marker, "match",
            ("0", true, Array.Empty<Material>()),
            ("BLUE", true, new[] { selected }));
        var nonMatch = await SeedRecipeTypeAsync(marker, "other",
            ("0", true, new[] { other }));

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&colorantId={selected.Id}", JsonOptions);

        list!.Should().ContainSingle(t => t.Id == match.Id);
        list.Should().NotContain(t => t.Id == nonMatch.Id);
    }

    [Fact]
    public async Task List_MaterialFilters_CombineAtTypeLevelAcrossDifferentProducts()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var plastic = await SeedMaterialAsync(MaterialType.Plastic);
        var colorant = await SeedMaterialAsync(MaterialType.Colorant);
        var both = await SeedRecipeTypeAsync(marker, "both",
            ("0", true, new[] { plastic }),
            ("BLUE", true, new[] { colorant }));
        var plasticOnly = await SeedRecipeTypeAsync(marker, "plastic",
            ("0", true, new[] { plastic }));
        var colorantOnly = await SeedRecipeTypeAsync(marker, "colorant",
            ("0", true, new[] { colorant }));

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&plasticId={plastic.Id}&colorantId={colorant.Id}", JsonOptions);

        list!.Should().ContainSingle(t => t.Id == both.Id);
        list.Should().NotContain(t => t.Id == plasticOnly.Id || t.Id == colorantOnly.Id);
    }

    [Fact]
    public async Task List_MaterialFilters_EnforceMaterialType()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var plastic = await SeedMaterialAsync(MaterialType.Plastic);
        var colorant = await SeedMaterialAsync(MaterialType.Colorant);
        await SeedRecipeTypeAsync(marker, "plastic", ("0", true, new[] { plastic }));
        await SeedRecipeTypeAsync(marker, "colorant", ("0", true, new[] { colorant }));

        var wrongPlastic = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&plasticId={colorant.Id}", JsonOptions);
        var wrongColorant = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&colorantId={plastic.Id}", JsonOptions);

        wrongPlastic.Should().BeEmpty();
        wrongColorant.Should().BeEmpty();
    }

    [Fact]
    public async Task List_MaterialFilter_DoesNotUseTaskRecipeCopies()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var plastic = await SeedMaterialAsync(MaterialType.Plastic);
        var type = await SeedRecipeTypeAsync(marker, "task-only",
            ("0", true, Array.Empty<Material>()));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = new ShiftTask
            {
                ImmId = _factory.TestImmId,
                MoldId = _factory.TestMoldId,
                ProductId = type.Products.Single().Id,
                PlanQuantity = 1,
                Cavities = 1,
                PlannedFullCycleSeconds = 10,
                PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow)
            };
            task.RecipeIngredients.Add(new ShiftTaskRecipeIngredient
            {
                MaterialId = plastic.Id,
                OriginalMaterialId = plastic.Id,
                Percent = 100
            });
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
        }

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&plasticId={plastic.Id}");

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_FiltersByCustomer()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var match = await SeedCustomerTypeAsync(marker, customer);
        var other = await SeedCustomerTypeAsync(marker, $"Другой {marker}");
        var differentCase = await SeedCustomerTypeAsync(marker, customer.ToUpperInvariant());

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?customer={Uri.EscapeDataString(customer)}");

        list!.Should().ContainSingle(t => t.Id == match.Id);
        list.Should().NotContain(t => t.Id == other.Id || t.Id == differentCase.Id);
    }

    [Fact]
    public async Task List_FiltersUnspecifiedCustomer()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var empty = await SeedCustomerTypeAsync(marker, null);
        var named = await SeedCustomerTypeAsync(marker, $"Заказчик {marker}");

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?search={marker}&customerEmpty=true");

        list!.Should().ContainSingle(t => t.Id == empty.Id);
        list.Should().NotContain(t => t.Id == named.Id);
    }

    [Fact]
    public async Task List_WhitespaceCustomerAndCustomerEmpty_TreatedAsUnspecified()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var empty = await SeedCustomerTypeAsync(marker, null);
        var named = await SeedCustomerTypeAsync(marker, $"Заказчик {marker}");

        var response = await client.GetAsync(
            $"/api/producttypes?search={marker}&customer=%20%20&customerEmpty=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<List<ProductTypeDto>>();
        list!.Should().ContainSingle(t => t.Id == empty.Id);
        list.Should().NotContain(t => t.Id == named.Id);
    }

    [Fact]
    public async Task List_CustomerFilterCombinesWithExistingFilters()
    {
        var client = await ManagerClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var customer = $"Заказчик {marker}";
        var active = await SeedCustomerTypeAsync(marker, customer, isActive: true);
        var archived = await SeedCustomerTypeAsync(marker, customer, isActive: false);
        var archivedOther = await SeedCustomerTypeAsync(marker, $"Другой {marker}", isActive: false);
        var archivedOutsideSearch = await SeedCustomerTypeAsync(
            $"outside-{Guid.NewGuid():N}", customer, isActive: false);

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?isActive=false&search={marker}&customer={Uri.EscapeDataString(customer)}");

        list!.Should().ContainSingle(t => t.Id == archived.Id);
        list.Should().NotContain(t => t.Id == active.Id || t.Id == archivedOther.Id);
        list.Should().NotContain(t => t.Id == archivedOutsideSearch.Id);
    }

    [Fact]
    public async Task List_UnknownCustomer_ReturnsEmpty()
    {
        var client = await ManagerClientAsync();
        var unknown = $"Несуществующий {Guid.NewGuid():N}";

        var list = await client.GetFromJsonAsync<List<ProductTypeDto>>(
            $"/api/producttypes?customer={Uri.EscapeDataString(unknown)}");

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_CustomerAndCustomerEmpty_ReturnsBadRequest()
    {
        var client = await ManagerClientAsync();

        var response = await client.GetAsync(
            "/api/producttypes?customer=ООО%20Ромашка&customerEmpty=true");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(
            "Параметры customer и customerEmpty нельзя передавать вместе.");
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
    public async Task ArticleSuggestion_UsesLatestTypeAndSkipsAllArticleConflicts()
    {
        var client = await ManagerClientAsync();
        async Task<ProductTypeDto> CreateType(string article)
        {
            var response = await client.PostAsJsonAsync("/api/producttypes",
                new CreateProductTypeRequestDto { Article = article, Name = article });
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<ProductTypeDto>())!;
        }

        var holder = await CreateType("ARTICLE");
        (await client.PostAsJsonAsync($"/api/producttypes/{holder.Id}/products",
                new CreateProductRequestDto { Article = "TYPE-SUGGEST-0102", Name = "Занятый полный артикул" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await CreateType("ARTICLE.TYPE-SUGGEST-0100");
        await CreateType("ARTICLE.TYPE-SUGGEST-0101");
        await CreateType("ARTICLE.TYPE-SUGGEST-0103");
        await CreateType("ARTICLE.TYPE-SUGGEST-0104.0");
        await CreateType("ARTICLE.TYPE-SUGGEST-0099");

        var response = await client.GetAsync("/api/producttypes/article-suggestion");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json", content);
        using var json = JsonDocument.Parse(content);

        // 0102 — полный артикул изделия, 0103 — артикул типа, 0104.0 — занятое изделие по умолчанию.
        json.RootElement.GetProperty("article").GetString().Should().Be("ARTICLE.TYPE-SUGGEST-0105");
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
