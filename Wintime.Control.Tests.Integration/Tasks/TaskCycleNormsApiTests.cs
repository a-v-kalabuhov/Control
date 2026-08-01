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

[Collection("Integration")]
public class TaskCycleNormsApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IntegrationTestFactory _factory;
    public TaskCycleNormsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private object CreateBody(object? workMode, int? full, int? injection) => new
    {
        immId = _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 100,
        workMode,
        plannedFullCycleSeconds = full,
        plannedInjectionCycleSeconds = injection
    };

    [Fact]
    public async Task CreateTask_WithoutFullCycle_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", CreateBody("Auto", null, null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateTask_WithFullCycleOnly_Returns201AndEchoesFields()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", CreateBody("SemiAuto", 60, null));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("workMode").GetString().Should().Be("SemiAuto");
        body.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(60);
        body.GetProperty("plannedInjectionCycleSeconds").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CreateTask_DefaultWorkMode_IsAuto()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", new
        {
            immId = _factory.TestImmId,
            moldId = _factory.TestMoldId,
            planQuantity = 100,
            plannedFullCycleSeconds = 45
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("workMode").GetString().Should().Be("Auto");
    }

    [Fact]
    public async Task CreateTask_InjectionCycleGreaterThanFullCycle_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/tasks", CreateBody("Auto", 60, 61));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_ChangesWorkModeAndNorms()
    {
        var client = await ManagerClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateBody("Auto", 60, 20));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetString()!;

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            workMode = "SemiAuto",
            plannedFullCycleSeconds = 90,
            plannedInjectionCycleSeconds = 30
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("workMode").GetString().Should().Be("SemiAuto");
        after.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(90);
        after.GetProperty("plannedInjectionCycleSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task UpdateTask_WithoutCycleFields_KeepsExistingValues()
    {
        var client = await ManagerClientAsync();
        var created = await client.PostAsJsonAsync("/api/tasks", CreateBody("SemiAuto", 75, 25));
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetString()!;

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { note = "только заметка" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("workMode").GetString().Should().Be("SemiAuto");
        after.GetProperty("plannedFullCycleSeconds").GetInt32().Should().Be(75);
        after.GetProperty("plannedInjectionCycleSeconds").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task UpdateTask_LegacyTaskWithoutFullCycle_SavesWithoutRequiringCycle()
    {
        Guid id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = new ShiftTask
            {
                ImmId = _factory.TestImmId,
                MoldId = _factory.TestMoldId,
                PlanQuantity = 100,
                Status = TaskStatus.Draft,
                PlannedFullCycleSeconds = null,
                PlannedInjectionCycleSeconds = null
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            id = task.Id;
        }

        var client = await ManagerClientAsync();

        // workMode обязательно передаём, чтобы cycleFieldsTouched == true в контроллере
        // и SetCycleNorms реально вызвался. plannedFullCycleSeconds намеренно не передаём —
        // fallback возьмёт текущее (null) значение legacy-задания, и требуется, чтобы
        // requireFullCycle: false пропустил это без ошибки. plannedInjectionCycleSeconds
        // не передаём — доменный метод законно запретит эталон цикла литья без эталона
        // полного цикла, что смазало бы смысл теста.
        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { workMode = "SemiAuto", note = "легаси-задание" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);
        after.GetProperty("plannedFullCycleSeconds").ValueKind.Should().Be(JsonValueKind.Null);
        after.GetProperty("workMode").GetString().Should().Be("SemiAuto");
    }
}
