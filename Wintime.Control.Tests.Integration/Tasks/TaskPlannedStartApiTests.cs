using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TaskPlannedStartApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private readonly IntegrationTestFactory _factory;
    public TaskPlannedStartApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private object Body(DateOnly? date, string? time) => new
    {
        immId = _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 10,
        plannedFullCycleSeconds = 30,
        plannedDate = date,
        plannedSetupStartTime = time
    };

    private async Task<DateTime> ExpectedStartAsync(DateOnly date, TimeOnly? time)
    {
        using var scope = _factory.Services.CreateScope();
        var schedule = await scope.ServiceProvider.GetRequiredService<IFactoryClock>().GetScheduleAsync();
        return schedule.PlannedStartUtc(date, time);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

    [Fact]
    public async Task CreateTask_WithDateOnly_PlannedStartIsFirstShiftStart()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(Tomorrow, null));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(resp);
        body.GetProperty("plannedDate").GetString().Should().Be(Tomorrow.ToString("yyyy-MM-dd"));
        body.GetProperty("plannedSetupStartTime").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("plannedStartAt").GetDateTime().ToUniversalTime()
            .Should().Be(await ExpectedStartAsync(Tomorrow, null));
        body.GetProperty("issuedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateTask_WithTime_PlannedStartIsDatePlusTime()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(Tomorrow, "14:00:00"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(resp);
        body.GetProperty("plannedSetupStartTime").GetString().Should().Be("14:00:00");
        body.GetProperty("plannedStartAt").GetDateTime().ToUniversalTime()
            .Should().Be(await ExpectedStartAsync(Tomorrow, new TimeOnly(14, 0)));
    }

    [Fact]
    public async Task CreateTask_WithoutPlannedDate_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(null, null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_PastDate_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", Body(Tomorrow.AddDays(-3), null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status, DateOnly date, TimeOnly? time = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 10,
            Status = status, PlannedDate = date, PlannedSetupStartTime = time, PlannedFullCycleSeconds = 30
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    [Fact]
    public async Task UpdateTask_IssuedTask_ChangesPlannedDate()
    {
        var id = await SeedTaskAsync(TaskStatus.Issued, Tomorrow);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}",
            new { plannedDate = Tomorrow.AddDays(2), plannedSetupStartTime = "08:30:00" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("plannedDate").GetString().Should().Be(Tomorrow.AddDays(2).ToString("yyyy-MM-dd"));
        after.GetProperty("plannedSetupStartTime").GetString().Should().Be("08:30:00");
    }

    [Fact]
    public async Task UpdateTask_ClearTime_SetsNull()
    {
        var id = await SeedTaskAsync(TaskStatus.Draft, Tomorrow, new TimeOnly(9, 0));
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { clearPlannedSetupStartTime = true });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("plannedSetupStartTime").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UpdateTask_InProgress_ChangePlan_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress, Tomorrow);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { plannedDate = Tomorrow.AddDays(1) });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_UnchangedPastDate_Succeeds()
    {
        var past = Tomorrow.AddDays(-5);
        var id = await SeedTaskAsync(TaskStatus.Issued, past);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { plannedDate = past, note = "правка" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
