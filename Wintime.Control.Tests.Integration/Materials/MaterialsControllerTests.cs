using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Wintime.Control.Core.DTOs.Material;
using Wintime.Control.Core.Enums;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Materials;

[Collection("Integration")]
public class MaterialsControllerTests : IClassFixture<IntegrationTestFactory>
{
    // Сервер шлёт type строкой (JsonStringEnumConverter в Program.cs); по умолчанию
    // ReadFromJsonAsync ждёт число и падает на "Plastic".
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public MaterialsControllerTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ManagerAsync() => ClientAsync("test_manager", "Manager123!");
    private Task<HttpClient> AdjusterAsync() => ClientAsync("test_adjuster", "Adjuster123!");

    private static string Uniq(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task<MaterialDto> CreateAsync(HttpClient client, CreateMaterialRequestDto req)
    {
        var resp = await client.PostAsJsonAsync("/api/materials", req);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<MaterialDto>(JsonOptions))!;
    }

    private static CreateMaterialRequestDto Plastic(string? article = null, string? name = null, string? manufacturer = null) =>
        new() { Type = MaterialType.Plastic, Article = article ?? Uniq("pp"), Name = name ?? Uniq("Полипропилен"), Manufacturer = manufacturer };

    [Fact]
    public async Task Create_NormalizesAndReturnsCreated()
    {
        var client = await ManagerAsync();
        var article = Uniq("pp");

        var dto = await CreateAsync(client, Plastic(article: $"  {article} ", manufacturer: "  Сибур "));

        dto.Article.Should().Be(article.ToUpperInvariant());
        dto.Manufacturer.Should().Be("Сибур");
        dto.Type.Should().Be(MaterialType.Plastic);
        dto.IsActive.Should().BeTrue();
        dto.IsUsed.Should().BeFalse();

        var get = await client.GetFromJsonAsync<MaterialDto>($"/api/materials/{dto.Id}", JsonOptions);
        get!.Article.Should().Be(article.ToUpperInvariant());
    }

    [Fact]
    public async Task Create_ColorantWithoutColor_Returns400()
    {
        var client = await ManagerAsync();
        var resp = await client.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequestDto { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель") });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("цвет");
    }

    [Fact]
    public async Task Create_WithoutType_Returns400()
    {
        var client = await ManagerAsync();
        var resp = await client.PostAsJsonAsync("/api/materials", new { article = Uniq("pp"), name = Uniq("Полипропилен") });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("тип");
    }

    [Fact]
    public async Task Create_DuplicateArticle_DifferentCase_Returns409()
    {
        var client = await ManagerAsync();
        var article = Uniq("pp");
        await CreateAsync(client, Plastic(article: article));

        var resp = await client.PostAsJsonAsync("/api/materials", Plastic(article: $" {article.ToLowerInvariant()} "));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("пластик");
    }

    [Fact]
    public async Task Create_DuplicateManufacturerAndName_CaseInsensitive_Returns409()
    {
        var client = await ManagerAsync();
        var name = Uniq("Полипропилен");
        await CreateAsync(client, Plastic(name: name, manufacturer: "Сибур"));

        var resp = await client.PostAsJsonAsync("/api/materials", Plastic(name: name.ToUpperInvariant(), manufacturer: "сибур"));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_DuplicateName_BothWithoutManufacturer_Returns409()
    {
        var client = await ManagerAsync();
        var name = Uniq("Без производителя");
        await CreateAsync(client, Plastic(name: name));

        var resp = await client.PostAsJsonAsync("/api/materials", Plastic(name: name));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SameArticleAsOtherType_IsAllowed()
    {
        var client = await ManagerAsync();
        var article = Uniq("x");
        await CreateAsync(client, Plastic(article: article));
        await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = article, Name = Uniq("Краситель"), ColorName = "Чёрный" });
    }

    [Fact]
    public async Task Update_ChangesFields_ClearsOptional_AndExcludesSelfFromUniqueness()
    {
        var client = await ManagerAsync();
        var created = await CreateAsync(client, Plastic(manufacturer: "Сибур"));

        // Тот же артикул (сам себя) + очистка производителя пустой строкой
        var resp = await client.PutAsJsonAsync($"/api/materials/{created.Id}",
            new UpdateMaterialRequestDto { Article = created.Article.ToLowerInvariant(), Manufacturer = "", Comment = "прим" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetFromJsonAsync<MaterialDto>($"/api/materials/{created.Id}", JsonOptions);
        get!.Manufacturer.Should().BeNull();
        get.Comment.Should().Be("прим");
        get.Article.Should().Be(created.Article);
    }

    [Fact]
    public async Task Update_ToOtherMaterialsArticle_Returns409()
    {
        var client = await ManagerAsync();
        var a = await CreateAsync(client, Plastic());
        var b = await CreateAsync(client, Plastic());

        var resp = await client.PutAsJsonAsync($"/api/materials/{b.Id}", new UpdateMaterialRequestDto { Article = a.Article });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ClearColorOfColorant_Returns400()
    {
        var client = await ManagerAsync();
        var c = await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = "Чёрный" });

        var resp = await client.PutAsJsonAsync($"/api/materials/{c.Id}", new UpdateMaterialRequestDto { ColorName = "" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_IgnoresType_TypeStaysTheSame()
    {
        var client = await ManagerAsync();
        var p = await CreateAsync(client, Plastic());

        // Лишнее поле type в теле не должно менять тип (в DTO его нет).
        var resp = await client.PutAsJsonAsync($"/api/materials/{p.Id}", new { type = "Colorant", name = p.Name });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await client.GetFromJsonAsync<MaterialDto>($"/api/materials/{p.Id}", JsonOptions);
        get!.Type.Should().Be(MaterialType.Plastic);
    }

    [Fact]
    public async Task Archive_AndRestore_ViaPut_AndFilterByActivity()
    {
        var client = await ManagerAsync();
        var p = await CreateAsync(client, Plastic());

        (await client.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { IsActive = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var archived = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Plastic&isActive=false&search={p.Article}", JsonOptions);
        archived!.Should().ContainSingle(x => x.Id == p.Id);

        (await client.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { IsActive = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var active = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Plastic&isActive=true&search={p.Article}", JsonOptions);
        active!.Should().ContainSingle(x => x.Id == p.Id);
    }

    [Fact]
    public async Task GetList_FiltersByType_AndSearchesManufacturerAndColor()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N");
        var plastic = await CreateAsync(client, Plastic(manufacturer: $"Завод {marker}"));
        var colorant = await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = $"Цвет {marker}" });

        var plastics = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Plastic&search={marker}", JsonOptions);
        plastics!.Select(x => x.Id).Should().BeEquivalentTo(new[] { plastic.Id });

        var colorants = await client.GetFromJsonAsync<List<MaterialDto>>($"/api/materials?type=Colorant&search={marker}", JsonOptions);
        colorants!.Select(x => x.Id).Should().BeEquivalentTo(new[] { colorant.Id });
    }

    [Fact]
    public async Task Delete_Unused_Returns204_ThenNotFound()
    {
        var client = await ManagerAsync();
        var p = await CreateAsync(client, Plastic());

        (await client.DeleteAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Suggestions_Manufacturers_DistinctAcrossTypes_IncludingArchived()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var manufacturer = $"Производитель {marker}";
        var p = await CreateAsync(client, Plastic(manufacturer: manufacturer));
        await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = "Белый", Manufacturer = manufacturer });
        await client.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { IsActive = false });

        var list = await client.GetFromJsonAsync<List<string>>($"/api/materials/suggestions/manufacturers?search={marker}");
        list.Should().Equal(manufacturer);
    }

    [Fact]
    public async Task Suggestions_Colors_ReturnsNameCodePairs()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        await CreateAsync(client, new CreateMaterialRequestDto
            { Type = MaterialType.Colorant, Article = Uniq("c"), Name = Uniq("Краситель"), ColorName = $"Чёрный {marker}", ColorCode = "RAL 9005" });

        var list = await client.GetFromJsonAsync<List<MaterialColorSuggestionDto>>($"/api/materials/suggestions/colors?search={marker}");
        list.Should().ContainSingle();
        list![0].Name.Should().Be($"Чёрный {marker}");
        list[0].Code.Should().Be("RAL 9005");
    }

    [Fact]
    public async Task Suggestions_AreLimitedTo20()
    {
        var client = await ManagerAsync();
        var marker = Guid.NewGuid().ToString("N")[..12];
        for (var i = 0; i < 22; i++)
            await CreateAsync(client, Plastic(manufacturer: $"Лимит {marker} {i:D2}"));

        var list = await client.GetFromJsonAsync<List<string>>($"/api/materials/suggestions/manufacturers?search={marker}");
        list.Should().HaveCount(20);
    }

    [Fact]
    public async Task Adjuster_CanRead_ButCannotWrite()
    {
        var manager = await ManagerAsync();
        var p = await CreateAsync(manager, Plastic());
        var adjuster = await AdjusterAsync();

        (await adjuster.GetAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await adjuster.PostAsJsonAsync("/api/materials", Plastic())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await adjuster.PutAsJsonAsync($"/api/materials/{p.Id}", new UpdateMaterialRequestDto { Name = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await adjuster.DeleteAsync($"/api/materials/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
