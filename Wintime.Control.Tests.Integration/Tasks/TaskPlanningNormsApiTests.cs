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
/// PZP-14: норматив наладки и буфер выпуска в задании — копирование умолчаний,
/// правила правки по статусам, расчётный план в TaskDto.
/// Тесты, зависящие от умолчаний, сами выставляют их перед проверкой (общая БД класса).
/// </summary>
[Collection("Integration")]
public class TaskPlanningNormsApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

    private readonly IntegrationTestFactory _factory;
    public TaskPlanningNormsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static async Task SetDefaultsAsync(HttpClient client, int setup, int fixedMinutes, int percent)
    {
        var resp = await client.PutAsJsonAsync("/api/settings/planning-defaults",
            new { setupMinutes = setup, releaseBufferFixedMinutes = fixedMinutes, releaseBufferPercent = percent });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> PostTaskAsync(HttpClient client, Dictionary<string, object?>? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["immId"] = _factory.TestImmId,
            ["moldId"] = _factory.TestMoldId,           // Cavities = 1
            ["productId"] = _factory.TestProductId,
            ["planQuantity"] = 100,
            ["plannedDate"] = Tomorrow.ToString("yyyy-MM-dd"),
            ["plannedFullCycleSeconds"] = 60
        };
        if (extra != null)
            foreach (var (k, v) in extra) body[k] = v;
        return await client.PostAsJsonAsync("/api/tasks", body);
    }

    private async Task<JsonElement> CreateTaskAsync(HttpClient client, Dictionary<string, object?>? extra = null)
    {
        var resp = await PostTaskAsync(client, extra);
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<Guid> SeedTaskAsync(TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = _factory.TestImmId, MoldId = _factory.TestMoldId, ProductId = _factory.TestProductId, PlanQuantity = 100, Note = "указание",
            Status = status, PlannedDate = Tomorrow, PlannedFullCycleSeconds = 60,
            PlannedSetupMinutes = 30, ReleaseBufferFixedMinutes = 10, ReleaseBufferPercent = 10
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/tasks/{id}", JsonOptions);

    [Fact]
    public async Task CreateTask_WithoutNormFields_UsesDefaults()
    {
        var client = await ManagerClientAsync();
        await SetDefaultsAsync(client, 45, 20, 15);

        var task = await CreateTaskAsync(client);

        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(45);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(20);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(15);
    }

    [Fact]
    public async Task CreateTask_WithNormFields_UsesProvidedValues()
    {
        var client = await ManagerClientAsync();
        await SetDefaultsAsync(client, 45, 20, 15);

        var task = await CreateTaskAsync(client, new()
        {
            ["plannedSetupMinutes"] = 120, ["releaseBufferFixedMinutes"] = 0, ["releaseBufferPercent"] = 5
        });

        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(120);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(0);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task CreateTask_NormOutOfRange_Returns400()
    {
        var client = await ManagerClientAsync();

        var resp = await PostTaskAsync(client, new() { ["plannedSetupMinutes"] = 1441 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePlanningDefaults_DoesNotChangeExistingTasks()
    {
        var client = await ManagerClientAsync();
        await SetDefaultsAsync(client, 10, 5, 5);
        var created = await CreateTaskAsync(client);

        await SetDefaultsAsync(client, 99, 99, 50);

        var task = await GetAsync(client, created.GetProperty("id").GetGuid());
        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(10);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(5);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task GetTask_ReturnsPlannedPlan()
    {
        var client = await ManagerClientAsync();
        var created = await CreateTaskAsync(client, new()
        {
            ["plannedSetupMinutes"] = 30, ["releaseBufferFixedMinutes"] = 10, ["releaseBufferPercent"] = 10
        });

        var task = await GetAsync(client, created.GetProperty("id").GetGuid());

        // 100 шт / 1 гнездо × 60 с = 6000; буфер 600 + 10 % от 6000 = 1200
        task.GetProperty("plannedReleaseSeconds").GetInt64().Should().Be(6000);
        task.GetProperty("plannedReleaseBufferSeconds").GetInt64().Should().Be(1200);
        var start = task.GetProperty("plannedStartAt").GetDateTime();
        var end = task.GetProperty("plannedEndAt").GetDateTime();
        (end - start).Should().Be(TimeSpan.FromSeconds(1800 + 6000 + 1200));
    }

    [Fact]
    public async Task UpdateTask_Issued_ChangesSetupNormAndBuffer()
    {
        var id = await SeedTaskAsync(TaskStatus.Issued);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}",
            new { plannedSetupMinutes = 50, releaseBufferFixedMinutes = 15 });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var task = await GetAsync(client, id);
        task.GetProperty("plannedSetupMinutes").GetInt32().Should().Be(50);
        task.GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(15);
        task.GetProperty("releaseBufferPercent").GetInt32().Should().Be(10);   // не передан — не меняется
    }

    [Fact]
    public async Task UpdateTask_InProgress_ChangesBuffer()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { releaseBufferPercent = 25 });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("releaseBufferPercent").GetInt32().Should().Be(25);
    }

    [Fact]
    public async Task UpdateTask_InProgress_FullFormPayloadWithBufferChange_Succeeds()
    {
        // Форма в режиме «только буфер» шлёт все поля задания без изменений + новый буфер.
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new
        {
            planQuantity = 100,
            note = "указание",
            workMode = "Auto",
            plannedFullCycleSeconds = 60,
            plannedDate = Tomorrow,
            clearPlannedSetupStartTime = true,
            plannedSetupMinutes = 30,
            releaseBufferFixedMinutes = 40,
            releaseBufferPercent = 10
        });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetAsync(client, id)).GetProperty("releaseBufferFixedMinutes").GetInt32().Should().Be(40);
    }

    [Fact]
    public async Task UpdateTask_InProgress_ChangeSetupNorm_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.InProgress);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { plannedSetupMinutes = 90 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetAsync(client, id)).GetProperty("plannedSetupMinutes").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task UpdateTask_Completed_ChangeBuffer_Returns400()
    {
        var id = await SeedTaskAsync(TaskStatus.Completed);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/tasks/{id}", new { releaseBufferFixedMinutes = 99 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
