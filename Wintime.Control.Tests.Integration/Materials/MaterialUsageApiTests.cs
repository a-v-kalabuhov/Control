using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Material;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Materials;

[Collection("Integration")]
public class MaterialUsageApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public MaterialUsageApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    private static async Task<Guid> NewMaterialAsync(HttpClient c)
    {
        var article = Uniq("M");
        var r = await c.PostAsJsonAsync("/api/materials", new CreateMaterialRequestDto
        {
            Type = MaterialType.Plastic, Article = article, Name = "Материал " + article
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<MaterialDto>(JsonOptions))!.Id;
    }

    private async Task<(Guid TypeId, Guid ProductId, Guid MoldId)> NewTypeWithMoldAsync(HttpClient c)
    {
        var article = Uniq("T");
        var r = await c.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "Тип " + article });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var type = (await r.Content.ReadFromJsonAsync<ProductTypeDto>(JsonOptions))!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-" + article, FormId = "F-" + article, Cavities = 1, IsActive = true, ProductTypeId = type.Id
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (type.Id, type.Products[0].Id, mold.Id);
    }

    private static Task<HttpResponseMessage> PutRecipeAsync(
        HttpClient c, Guid typeId, Guid productId, params (Guid MaterialId, decimal Percent)[] rows) =>
        c.PutAsJsonAsync($"/api/producttypes/{typeId}/products/{productId}/recipe",
            new { ingredients = rows.Select(r => new { materialId = r.MaterialId, percent = r.Percent }) });

    private static async Task<bool> IsUsedAsync(HttpClient c, Guid materialId)
    {
        var dto = (await c.GetFromJsonAsync<MaterialDto>($"/api/materials/{materialId}", JsonOptions))!;
        return dto.IsUsed;
    }

    [Fact]
    public async Task MaterialInProductRecipe_IsUsed_AndDeleteReturns409()
    {
        var c = await ManagerAsync();
        var (typeId, productId, _) = await NewTypeWithMoldAsync(c);
        var material = await NewMaterialAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (material, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await IsUsedAsync(c, material)).Should().BeTrue();
        (await c.DeleteAsync($"/api/materials/{material}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task MaterialInTaskCopy_IsUsed_AndDeleteReturns409()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId) = await NewTypeWithMoldAsync(c);
        var material = await NewMaterialAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (material, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var taskResp = await c.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, productId,
            planQuantity = 10,
            plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            plannedFullCycleSeconds = 60
        });
        taskResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Рецепт изделия очищаем — материал держит только копия задания.
        (await PutRecipeAsync(c, typeId, productId)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await IsUsedAsync(c, material)).Should().BeTrue();
        (await c.DeleteAsync($"/api/materials/{material}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReplacedOutMaterial_IsStillUsed_AndDeleteReturns409()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId) = await NewTypeWithMoldAsync(c);
        var original = await NewMaterialAsync(c);
        var replacement = await NewMaterialAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (original, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var taskResp = await c.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, productId,
            planQuantity = 10,
            plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            plannedFullCycleSeconds = 60
        });
        var task = (await taskResp.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;
        var row = task.RecipeIngredients.Single();
        (await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = replacement }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PutRecipeAsync(c, typeId, productId)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Исходный материал больше нигде не текущий — держит только OriginalMaterialId.
        (await IsUsedAsync(c, original)).Should().BeTrue();
        (await c.DeleteAsync($"/api/materials/{original}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UnusedMaterial_IsDeletable()
    {
        var c = await ManagerAsync();
        var material = await NewMaterialAsync(c);

        (await IsUsedAsync(c, material)).Should().BeFalse();
        (await c.DeleteAsync($"/api/materials/{material}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
