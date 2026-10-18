using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using ImmEntity = Wintime.Control.Core.Entities.Imm;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskMoldCompatibilityGuardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskMoldCompatibilityGuardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    /// ПФ с типом изделия; compatibleImmIds — её список совместимых ТПА.
    private async Task<Guid> SeedMoldAsync(params Guid[] compatibleImmIds)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imms = await db.Imms.Where(i => compatibleImmIds.Contains(i.Id)).ToListAsync();
        var mold = new Mold
        {
            Name = "ПФ", FormId = $"PF-{Guid.NewGuid():N}", Cavities = 1, MaxResourceCycles = 1000,
            IsActive = true, ProductTypeId = _factory.TestProductTypeId, CompatibleImms = imms
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private async Task<Guid> SeedOtherImmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new ImmEntity { Name = $"ТПА-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    private async Task<HttpResponseMessage> CreateTaskAsync(Guid moldId)
    {
        var client = await ManagerClientAsync();
        return await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, productId = _factory.TestProductId, planQuantity = 10, note = "x",
            plannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            plannedFullCycleSeconds = 30
        });
    }

    [Fact]
    public async Task CreateTask_MoldWithEmptyList_Returns201()
    {
        var resp = await CreateTaskAsync(await SeedMoldAsync());
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateTask_ImmInMoldList_Returns201()
    {
        var resp = await CreateTaskAsync(await SeedMoldAsync(_factory.TestImmId));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateTask_ImmNotInMoldList_Returns400()
    {
        var moldId = await SeedMoldAsync(await SeedOtherImmAsync());

        var resp = await CreateTaskAsync(moldId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("не совместима");
    }
}
