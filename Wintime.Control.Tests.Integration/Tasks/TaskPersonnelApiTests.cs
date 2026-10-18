using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskPersonnelApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IntegrationTestFactory _factory;
    public TaskPersonnelApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status, string? personnelId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, ProductId = _factory.TestProductId, PlanQuantity = 10,
            Status = status, PersonnelId = personnelId, PlannedFullCycleSeconds = 30,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<string> CreateUserAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = $"u_{Guid.NewGuid():N}";
        var user = new User { UserName = name, Email = $"{name}@test.local", FullName = name, Role = role, IsActive = true };
        (await um.CreateAsync(user, "Passw0rd!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<JsonElement> GetAsync(HttpClient c, Guid id) =>
        await c.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);

    [Fact]
    public async Task SetPersonnel_Planned_AssignsAndClears()
    {
        var id = await SeedTaskAsync(TaskStatus.Planned, null);
        var client = await ManagerClientAsync();

        (await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = _factory.TestAdjusterId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("personnelId").GetString().Should().Be(_factory.TestAdjusterId);

        (await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("personnelId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SetPersonnel_Issued_Reassigns()
    {
        var other = await CreateUserAsync(UserRole.Adjuster);
        var id = await SeedTaskAsync(TaskStatus.Issued, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = other });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("personnelId").GetString().Should().Be(other);
    }

    [Fact]
    public async Task SetPersonnel_Issued_Clear_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.Issued, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetPersonnel_InProgress_Returns400()
    {
        var other = await CreateUserAsync(UserRole.Adjuster); // валидный наладчик — отказ именно из-за статуса
        var id = await SeedTaskAsync(TaskStatus.InProgress, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = other });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetPersonnel_NotAdjuster_Returns400()
    {
        var manager = await CreateUserAsync(UserRole.Manager);
        var id = await SeedTaskAsync(TaskStatus.Planned, null);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}/personnel", new { personnelId = manager });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IssueTask_WithPersonnelInBody_AssignsAndIssues()
    {
        var id = await SeedTaskAsync(TaskStatus.Planned, null);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { personnelId = _factory.TestAdjusterId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await GetAsync(client, id);
        after.GetProperty("status").GetString().Should().Be("Issued");
        after.GetProperty("personnelId").GetString().Should().Be(_factory.TestAdjusterId);
    }

    [Fact]
    public async Task IssueTask_AlreadyAssigned_NoBody_Issues()
    {
        var id = await SeedTaskAsync(TaskStatus.Planned, _factory.TestAdjusterId);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IssueTask_NoPersonnel_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.Planned, null);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{id}/issue", new { });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("Planned");
    }
}
