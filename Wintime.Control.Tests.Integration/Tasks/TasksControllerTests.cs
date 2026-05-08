using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Tasks;

[Collection("Integration")]
public class TasksControllerTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public TasksControllerTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // =========================================================================
    // Авторизация
    // =========================================================================

    /// <summary>
    /// GET /api/tasks без токена должен вернуть 401 — список заданий
    /// недоступен анонимным пользователям.
    /// </summary>
    [Fact]
    public async Task GetTasks_WithoutToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/tasks");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// POST /api/tasks с токеном наблюдателя (Observer) должен вернуть 403 —
    /// создавать задания может только Менеджер или Администратор.
    /// </summary>
    [Fact]
    public async Task CreateTask_AsObserver_Returns403()
    {
        var client = await CreateAuthenticatedClientAsync("test_observer", "Observer123!");

        var response = await client.PostAsJsonAsync("/api/tasks", MakeCreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================
    // Жизненный цикл задания: Draft → InProgress → Completed → Closed
    // =========================================================================

    /// <summary>
    /// Менеджер создаёт задание → статус Draft, код ответа 201.
    /// </summary>
    [Fact]
    public async Task CreateTask_AsManager_Returns201WithDraftStatus()
    {
        var client = await CreateAuthenticatedClientAsync("test_manager", "Manager123!");

        var response = await client.PostAsJsonAsync("/api/tasks", MakeCreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("status").GetInt32().Should().Be(0, "0 = Draft");
    }

    /// <summary>
    /// Наладчик запускает задание → статус InProgress, код ответа 200.
    /// </summary>
    [Fact]
    public async Task StartTask_AsAdjuster_Returns200AndInProgressStatus()
    {
        var managerClient = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");

        var taskId = await CreateTaskAsync(managerClient);

        var startResponse = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/start",
            new { moldQr = "TEST-QR", immQr = "IMM-QR" });

        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await adjusterClient.GetAsync($"/api/tasks/{taskId}");
        var body = await taskResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("status").GetInt32().Should().Be(2, "2 = InProgress");
    }

    /// <summary>
    /// Попытка запустить задание повторно (уже InProgress) должна вернуть 400.
    /// </summary>
    [Fact]
    public async Task StartTask_AlreadyInProgress_Returns400()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");

        var taskId = await CreateTaskAsync(managerClient);
        await adjusterClient.PostAsJsonAsync($"/api/tasks/{taskId}/start",
            new { moldQr = "TEST-QR", immQr = "IMM-QR" });

        var secondStart = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/start",
            new { moldQr = "TEST-QR", immQr = "IMM-QR" });

        secondStart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Наладчик завершает задание → статус Completed, код ответа 200.
    /// </summary>
    [Fact]
    public async Task CompleteTask_AsAdjuster_Returns200AndCompletedStatus()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");

        var taskId = await CreateTaskAsync(managerClient);
        await adjusterClient.PostAsJsonAsync($"/api/tasks/{taskId}/start",
            new { moldQr = "TEST-QR", immQr = "IMM-QR" });

        var completeResponse = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/complete",
            new { actualQuantity = 100 });

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await adjusterClient.GetAsync($"/api/tasks/{taskId}");
        var body = await taskResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("status").GetInt32().Should().Be(3, "3 = Completed");
    }

    /// <summary>
    /// Наладчик не может закрыть задание — это право Менеджера/Администратора,
    /// поэтому должен вернуться 403.
    /// </summary>
    [Fact]
    public async Task CloseTask_AsAdjuster_Returns403()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");

        var taskId = await CreateTaskAsync(managerClient);

        var response = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/close",
            new { closeReason = "test" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Менеджер закрывает задание → статус Closed, код ответа 200.
    /// </summary>
    [Fact]
    public async Task CloseTask_AsManager_Returns200AndClosedStatus()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager", "Manager123!");

        var taskId = await CreateTaskAsync(managerClient);

        var response = await managerClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/close",
            new { closeReason = "end of day" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await managerClient.GetAsync($"/api/tasks/{taskId}");
        var body = await taskResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("status").GetInt32().Should().Be(4, "4 = Closed");
    }

    // =========================================================================
    // Несуществующее задание
    // =========================================================================

    /// <summary>
    /// GET /api/tasks/{id} для несуществующего GUID должен вернуть 404.
    /// </summary>
    [Fact]
    public async Task GetTaskById_NonExistentId_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync("test_manager", "Manager123!");

        var response = await client.GetAsync($"/api/tasks/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private object MakeCreateRequest() => new
    {
        immId = _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 100,
        note = "Integration test task"
    };

    private async Task<Guid> CreateTaskAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", MakeCreateRequest());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }
}
