using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Material;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskRecipeApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IntegrationTestFactory _factory;
    public TaskRecipeApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string Uniq(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12].ToUpperInvariant();

    /// <summary>Тип + изделие «0» + ПФ этого типа + два пластика: (typeId, productId, moldId, plasticA, plasticB).</summary>
    private async Task<(Guid TypeId, Guid ProductId, Guid MoldId, Guid PlasticA, Guid PlasticB)> ArrangeAsync(HttpClient c)
    {
        var article = Uniq("T");
        var typeResp = await c.PostAsJsonAsync("/api/producttypes",
            new CreateProductTypeRequestDto { Article = article, Name = "Тип " + article });
        typeResp.StatusCode.Should().Be(HttpStatusCode.Created, await typeResp.Content.ReadAsStringAsync());
        var type = (await typeResp.Content.ReadFromJsonAsync<ProductTypeDto>(JsonOptions))!;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-" + article, FormId = "F-" + article, Cavities = 1, IsActive = true, ProductTypeId = type.Id
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        return (type.Id, type.Products[0].Id, mold.Id,
                await NewMaterialAsync(c, MaterialType.Plastic),
                await NewMaterialAsync(c, MaterialType.Plastic));
    }

    private static async Task<Guid> NewMaterialAsync(HttpClient c, MaterialType type)
    {
        var article = Uniq("M");
        var r = await c.PostAsJsonAsync("/api/materials", new CreateMaterialRequestDto
        {
            Type = type, Article = article, Name = "Материал " + article,
            ColorName = type == MaterialType.Colorant ? "Чёрный" : null
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<MaterialDto>(JsonOptions))!.Id;
    }

    private static Task<HttpResponseMessage> PutRecipeAsync(
        HttpClient c, Guid typeId, Guid productId, params (Guid MaterialId, decimal Percent)[] rows) =>
        c.PutAsJsonAsync($"/api/producttypes/{typeId}/products/{productId}/recipe",
            new { ingredients = rows.Select(r => new { materialId = r.MaterialId, percent = r.Percent }) });

    private static object CreateBody(Guid immId, Guid moldId, Guid productId) => new
    {
        immId, moldId, productId,
        planQuantity = 100,
        plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
        plannedFullCycleSeconds = 60
    };

    private static async Task<TaskDto> CreateTaskAsync(HttpClient c, Guid immId, Guid moldId, Guid productId)
    {
        var resp = await c.PostAsJsonAsync("/api/tasks", CreateBody(immId, moldId, productId));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;
    }

    [Fact]
    public async Task CreateTask_CopiesProductRecipe()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 60m), (b, 40m))).StatusCode.Should().Be(HttpStatusCode.OK);

        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);

        task.RecipeIngredients.Should().HaveCount(2);
        task.RecipeIngredients.Select(i => i.MaterialId).Should().Equal(a, b);
        task.RecipeIngredients.Should().OnlyContain(i => i.MaterialId == i.OriginalMaterialId);
        task.RecipeIngredients.Select(i => i.Percent).Should().Equal(60m, 40m);
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public async Task CreateTask_WithoutProductRecipe_HasEmptyCopy()
    {
        var c = await ManagerAsync();
        var (_, productId, moldId, _, _) = await ArrangeAsync(c);

        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);

        task.RecipeIngredients.Should().BeEmpty();
        task.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public async Task EditProductRecipe_DoesNotChangeExistingTaskCopy()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);

        (await PutRecipeAsync(c, typeId, productId, (b, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);

        var reread = (await c.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions))!;
        reread.RecipeIngredients.Should().ContainSingle().Which.MaterialId.Should().Be(a);
    }

    [Fact]
    public async Task UpdateTask_ProductChange_OverwritesCopy()
    {
        var c = await ManagerAsync();
        var (typeId, firstProduct, moldId, a, _) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, firstProduct, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Второе изделие того же типа + свой рецепт из другого пластика.
        var secondResp = await c.PostAsJsonAsync($"/api/producttypes/{typeId}/products",
            new CreateProductRequestDto { Article = "B", Name = "Второе" });
        secondResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = (await secondResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;
        var other = await NewMaterialAsync(c, MaterialType.Plastic);
        (await PutRecipeAsync(c, typeId, second.Id, (other, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);

        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, firstProduct);

        var resp = await c.PutAsJsonAsync($"/api/tasks/{task.Id}", new { productId = second.Id });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent, await resp.Content.ReadAsStringAsync());
        var reread = (await c.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions))!;
        reread.RecipeIngredients.Should().ContainSingle().Which.MaterialId.Should().Be(other);
        reread.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTask_SameProduct_KeepsEditedCopy()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);
        var row = task.RecipeIngredients.Single();
        (await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = b }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await c.PutAsJsonAsync($"/api/tasks/{task.Id}", new { productId });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var reread = (await c.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions))!;
        reread.RecipeIngredients.Should().ContainSingle().Which.MaterialId.Should().Be(b);
        reread.RecipeEdited.Should().BeTrue();
    }

    [Fact]
    public async Task ReplaceIngredient_Manager_Returns200AndMarksEdited()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);
        var row = task.RecipeIngredients.Single();

        var resp = await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = b });

        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var dto = (await resp.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;
        dto.RecipeEdited.Should().BeTrue();
        dto.RecipeIngredients.Single().MaterialId.Should().Be(b);
        dto.RecipeIngredients.Single().OriginalMaterialId.Should().Be(a);
        dto.RecipeIngredients.Single().OriginalMaterialArticle.Should().NotBeNullOrEmpty();

        // Копия сохранилась в БД, не только в ответе.
        var reread = (await c.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions))!;
        reread.RecipeIngredients.Single().MaterialId.Should().Be(b);
    }

    [Fact]
    public async Task ReplaceIngredient_MaterialAlreadyUsedByAnotherRow_Returns400AndKeepsPersistedCopy()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 60m), (b, 40m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);
        var first = task.RecipeIngredients.Single(i => i.MaterialId == a);

        var resp = await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{first.Id}", new { materialId = b });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, await resp.Content.ReadAsStringAsync());
        var reread = (await c.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", JsonOptions))!;
        reread.RecipeIngredients.Select(i => i.MaterialId).Should().BeEquivalentTo(new[] { a, b });
        reread.RecipeIngredients.Should().OnlyContain(i => i.MaterialId == i.OriginalMaterialId);
        reread.RecipeEdited.Should().BeFalse();
    }

    [Fact]
    public void TaskRecipe_CurrentMaterialPerTaskIndex_IsUnique()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var entityType = db.Model.FindEntityType(typeof(ShiftTaskRecipeIngredient))!;

        var index = entityType.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(ShiftTaskRecipeIngredient.ShiftTaskId), nameof(ShiftTaskRecipeIngredient.MaterialId) }));

        index.IsUnique.Should().BeTrue();
        index.GetDatabaseName().Should().Be("IX_ShiftTaskRecipeIngredients_ShiftTaskId_MaterialId");
    }

    [Fact]
    public async Task ReplaceIngredient_BackToOriginal_ResetsEdited()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);
        var row = task.RecipeIngredients.Single();
        (await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = b }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = a });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await resp.Content.ReadFromJsonAsync<TaskDto>(JsonOptions))!;
        dto.RecipeEdited.Should().BeFalse();
        dto.RecipeIngredients.Single().MaterialId.Should().Be(a);
    }

    [Fact]
    public async Task ReplaceIngredient_Adjuster_Returns403()
    {
        var manager = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(manager);
        (await PutRecipeAsync(manager, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(manager, _factory.TestImmId, moldId, productId);
        var row = task.RecipeIngredients.Single();
        var adjuster = _factory.CreateClient();
        AuthHelper.SetBearerToken(adjuster, (await AuthHelper.GetTokenAsync(adjuster, "test_adjuster", "Adjuster123!"))!);

        var resp = await adjuster.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = b });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReplaceIngredient_OtherType_Returns400()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, _) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);
        var row = task.RecipeIngredients.Single();
        var colorant = await NewMaterialAsync(c, MaterialType.Colorant);

        var resp = await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = colorant });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReplaceIngredient_UnknownIngredientOrMaterial_Returns404()
    {
        var c = await ManagerAsync();
        var (typeId, productId, moldId, a, b) = await ArrangeAsync(c);
        (await PutRecipeAsync(c, typeId, productId, (a, 100m))).StatusCode.Should().Be(HttpStatusCode.OK);
        var task = await CreateTaskAsync(c, _factory.TestImmId, moldId, productId);
        var row = task.RecipeIngredients.Single();

        (await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{Guid.NewGuid()}", new { materialId = b }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/tasks/{task.Id}/recipe/{row.Id}", new { materialId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PutAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/recipe/{row.Id}", new { materialId = b }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
