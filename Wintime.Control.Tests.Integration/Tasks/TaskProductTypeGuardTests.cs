using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskProductTypeGuardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public TaskProductTypeGuardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Fact]
    public async Task CreateTask_ForMoldWithoutType_Returns400()
    {
        Guid moldId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var mold = new Mold { Name = "NoType", FormId = $"PF-{Guid.NewGuid():N}",
                                  Cavities = 1, IsActive = true, ProductTypeId = null };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();
            moldId = mold.Id;
        }

        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId, planQuantity = 10, note = "x",
            plannedFullCycleSeconds = 30
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_ForMoldWithType_Returns201()
    {
        // TestMoldId сидируется с TestProductTypeId → задание создаётся.
        var client = await ManagerClientAsync();
        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId, moldId = _factory.TestMoldId, planQuantity = 10, note = "x",
            plannedFullCycleSeconds = 30
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
