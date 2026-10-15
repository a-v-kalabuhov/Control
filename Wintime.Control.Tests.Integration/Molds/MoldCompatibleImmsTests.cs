using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using ImmEntity = Wintime.Control.Core.Entities.Imm;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldCompatibleImmsTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldCompatibleImmsTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedImmAsync(string name, bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new ImmEntity { Name = name, TemplateId = _factory.TestTemplateId, IsActive = isActive };
        db.Imms.Add(imm);
        await db.SaveChangesAsync();
        return imm.Id;
    }

    /// Создаёт ПФ напрямую в БД с заданными совместимыми ТПА (в обход API — чтобы
    /// заранее привязать уже архивный ТПА).
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

    private CreateMoldRequestDto NewMold(params Guid[] compatibleImmIds) => new()
    {
        FormId = $"PF-{Guid.NewGuid():N}", Name = "ПФ", Cavities = 1, MaxResourceCycles = 1000,
        ProductTypeId = _factory.TestProductTypeId, CompatibleImmIds = compatibleImmIds.ToList()
    };

    private static async Task<MoldDto> GetMoldAsync(HttpClient client, Guid id)
        => (await client.GetFromJsonAsync<MoldDto>($"/api/molds/{id}"))!;

    [Fact]
    public async Task CreateMold_WithCompatibleImms_ReturnsSortedListInDto()
    {
        var client = await ManagerClientAsync();
        var b = await SeedImmAsync("ТПА-B");
        var a = await SeedImmAsync("ТПА-A");

        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(b, a, a));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<MoldDto>();
        dto!.CompatibleImms.Select(i => i.Name).Should().Equal("ТПА-A", "ТПА-B");
        dto.CompatibleImms.Should().OnlyContain(i => i.IsActive);
    }

    [Fact]
    public async Task CreateMold_WithoutCompatibleImms_HasEmptyList()
    {
        var client = await ManagerClientAsync();
        var request = NewMold();
        request.CompatibleImmIds = null;

        var resp = await client.PostAsJsonAsync("/api/molds", request);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        (await resp.Content.ReadFromJsonAsync<MoldDto>())!.CompatibleImms.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateMold_WithUnknownImm_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(Guid.NewGuid()));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithArchivedImm_Returns400()
    {
        var client = await ManagerClientAsync();
        var archived = await SeedImmAsync("ТПА-архив", isActive: false);
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(archived));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMold_NullList_KeepsCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var imm = await SeedImmAsync("ТПА-1");
        var moldId = await SeedMoldAsync(imm);

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new UpdateMoldRequestDto { Name = "Новое" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetMoldAsync(client, moldId)).CompatibleImms.Select(i => i.Id).Should().Equal(imm);
    }

    [Fact]
    public async Task UpdateMold_EmptyList_ClearsCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var moldId = await SeedMoldAsync(await SeedImmAsync("ТПА-1"));

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid>() });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetMoldAsync(client, moldId)).CompatibleImms.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateMold_List_ReplacesCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var oldImm = await SeedImmAsync("ТПА-old");
        var newImm = await SeedImmAsync("ТПА-new");
        var moldId = await SeedMoldAsync(oldImm);

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid> { newImm } });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetMoldAsync(client, moldId)).CompatibleImms.Select(i => i.Id).Should().Equal(newImm);
    }

    [Fact]
    public async Task UpdateMold_AddArchivedImm_Returns400()
    {
        var client = await ManagerClientAsync();
        var moldId = await SeedMoldAsync();
        var archived = await SeedImmAsync("ТПА-архив", isActive: false);

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid> { archived } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateMold_KeepPreviouslyLinkedArchivedImm_Allowed()
    {
        var client = await ManagerClientAsync();
        var archivedLater = await SeedImmAsync("ТПА-уйдёт-в-архив");
        var active = await SeedImmAsync("ТПА-активный");
        var moldId = await SeedMoldAsync(archivedLater);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = await db.Imms.FindAsync(archivedLater);
            imm!.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { CompatibleImmIds = new List<Guid> { archivedLater, active } });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dto = await GetMoldAsync(client, moldId);
        dto.CompatibleImms.Should().HaveCount(2);
        dto.CompatibleImms.Single(i => i.Id == archivedLater).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetMoldList_ContainsCompatibleImms()
    {
        var client = await ManagerClientAsync();
        var imm = await SeedImmAsync("ТПА-список");
        var moldId = await SeedMoldAsync(imm);

        var list = await client.GetFromJsonAsync<List<MoldDto>>("/api/molds");
        list!.Single(m => m.Id == moldId).CompatibleImms.Select(i => i.Id).Should().Equal(imm);
    }
}
