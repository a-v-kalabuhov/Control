using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Mold;
using Wintime.Control.Core.DTOs.ProductType;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Molds;

[Collection("Integration")]
public class MoldProductTypeTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public MoldProductTypeTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedTypeAsync(bool isActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var pt = new ProductType { Article = $"PT-{Guid.NewGuid():N}", Name = "Изд", IsActive = isActive };
        db.ProductTypes.Add(pt);
        await db.SaveChangesAsync();
        return pt.Id;
    }

    private static CreateMoldRequestDto NewMold(Guid? typeId) => new()
    {
        FormId = $"PF-{Guid.NewGuid():N}", Name = "ПФ", Cavities = 1,
        MaxResourceCycles = 1000, ProductTypeId = typeId
    };

    [Fact]
    public async Task CreateMold_WithoutProductType_Returns400()
    {
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(null));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithArchivedType_Returns400()
    {
        var client = await ManagerClientAsync();
        var archived = await SeedTypeAsync(isActive: false);
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(archived));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateMold_WithType_BindsAndDtoHasTypeName()
    {
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        var resp = await client.PostAsJsonAsync("/api/molds", NewMold(typeId));
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await resp.Content.ReadFromJsonAsync<MoldDto>();
        dto!.ProductTypeId.Should().Be(typeId);
        dto.ProductTypeName.Should().Be("Изд");
    }

    [Fact]
    public async Task UpdateMold_SetTypeOnLegacyMold_NullToValue_Allowed_EvenWithTasks()
    {
        // Legacy ПФ без типа, но с заданием → присвоение типа разрешено (исключение п.7).
        var client = await ManagerClientAsync();
        var typeId = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "Legacy", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = null };
            db.Molds.Add(mold);
            db.ShiftTasks.Add(new Core.Entities.ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = mold.Id, PlanQuantity = 10,
                Status = Core.Enums.TaskStatus.Draft, IssuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = typeId });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateMold_ChangeExistingType_WithTasks_Returns409()
    {
        var client = await ManagerClientAsync();
        var type1 = await SeedTypeAsync();
        var type2 = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "M", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = type1 };
            db.Molds.Add(mold);
            db.ShiftTasks.Add(new Core.Entities.ShiftTask
            {
                ImmId = _factory.TestImmId, MoldId = mold.Id, PlanQuantity = 10,
                Status = Core.Enums.TaskStatus.Completed, IssuedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = type2 });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateMold_ChangeExistingType_NoTasks_Allowed()
    {
        var client = await ManagerClientAsync();
        var type1 = await SeedTypeAsync();
        var type2 = await SeedTypeAsync();
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "M", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = type1 };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}",
            new UpdateMoldRequestDto { ProductTypeId = type2 });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
