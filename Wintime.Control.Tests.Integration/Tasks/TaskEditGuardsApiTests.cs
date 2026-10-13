using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

/// <summary>
/// BL-39: PUT /api/tasks/{id} не меняет статус и не правит параметры задания вне «Планируется».
/// </summary>
[Collection("Integration")]
public class TaskEditGuardsApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private readonly IntegrationTestFactory _factory;
    public TaskEditGuardsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, PlanQuantity = 100, Note = "указание",
            Status = status, PlannedDate = Tomorrow, PlannedFullCycleSeconds = 60
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<JsonElement> GetAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);

    [Fact]
    public async Task UpdateTask_StatusInBody_IsIgnored()
    {
        var id = await SeedTaskAsync(TaskStatus.Planned);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { status = "Issued" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("status").GetString().Should().Be("Planned");
    }

    [Fact]
    public async Task UpdateTask_PlanQuantityInProgress_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { planQuantity = 999 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(client, id)).GetProperty("planQuantity").GetInt32().Should().Be(100);
    }

    [Fact]
    public async Task UpdateTask_IssuedWithUnchangedFormPayload_Succeeds()
    {
        // Форма менеджера в «Выдано» шлёт все поля, меняя только плановое начало.
        var id = await SeedTaskAsync(TaskStatus.Issued);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            planQuantity = 100,
            note = "указание",
            workMode = "Auto",
            plannedFullCycleSeconds = 60,
            plannedDate = Tomorrow.AddDays(1)
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("plannedDate").GetString()
            .Should().Be(Tomorrow.AddDays(1).ToString("yyyy-MM-dd"));
    }
}
