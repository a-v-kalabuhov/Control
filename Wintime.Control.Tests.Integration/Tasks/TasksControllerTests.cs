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
        body.GetProperty("status").GetString().Should().Be("Draft");
    }

    /// <summary>
    /// Наладчик запускает задание → статус InProgress, код ответа 200.
    /// Полный цикл: Draft → Issue → Start (Setup) → CompleteSetup (InProgress).
    /// </summary>
    [Fact]
    public async Task StartTask_AsAdjuster_Returns200AndInProgressStatus()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");
        var immId = await _factory.CreateFreshImmAsync();

        var taskId = await CreateTaskAsync(managerClient, immId);
        await IssueTaskAsync(managerClient, taskId);

        var startResponse = await adjusterClient.PostAsync($"/api/tasks/{taskId}/start", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var setupResponse = await adjusterClient.PostAsync($"/api/tasks/{taskId}/complete-setup", null);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await adjusterClient.GetAsync($"/api/tasks/{taskId}");
        var body = await taskResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("status").GetString().Should().Be("InProgress");
    }

    /// <summary>
    /// Попытка запустить задание повторно (уже Setup) должна вернуть 400.
    /// </summary>
    [Fact]
    public async Task StartTask_AlreadyInProgress_Returns400()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");
        var immId = await _factory.CreateFreshImmAsync();

        var taskId = await CreateTaskAsync(managerClient, immId);
        await IssueTaskAsync(managerClient, taskId);
        await adjusterClient.PostAsync($"/api/tasks/{taskId}/start", null);

        var secondStart = await adjusterClient.PostAsync($"/api/tasks/{taskId}/start", null);

        secondStart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// BL-28: нельзя начать наладку по заданию, если на том же ТПА уже есть
    /// активное задание (Setup/InProgress). Второй /start должен вернуть 400.
    /// </summary>
    [Fact]
    public async Task StartTask_WhenAnotherTaskActiveOnSameImm_Returns400()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");
        var immId = await _factory.CreateFreshImmAsync();

        // Первое задание доводим до активного статуса (Setup) на этом ТПА.
        var firstTaskId = await CreateTaskAsync(managerClient, immId);
        await IssueTaskAsync(managerClient, firstTaskId);
        var firstStart = await adjusterClient.PostAsync($"/api/tasks/{firstTaskId}/start", null);
        firstStart.StatusCode.Should().Be(HttpStatusCode.OK);

        // Второе задание на том же ТПА — запуск наладки должен быть отклонён.
        var secondTaskId = await CreateTaskAsync(managerClient, immId);
        await IssueTaskAsync(managerClient, secondTaskId);
        var secondStart = await adjusterClient.PostAsync($"/api/tasks/{secondTaskId}/start", null);

        secondStart.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// PZP-08: наладчик увеличивает план задания в работе (InProgress).
    /// POST /add-quantity прибавляет delta к PlanQuantity, статус не меняется.
    /// </summary>
    [Fact]
    public async Task AddQuantity_InProgress_IncreasesPlanQuantity()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");
        var immId = await _factory.CreateFreshImmAsync();

        var taskId = await CreateTaskAsync(managerClient, immId); // план 100
        await IssueTaskAsync(managerClient, taskId);
        await adjusterClient.PostAsync($"/api/tasks/{taskId}/start", null);
        await adjusterClient.PostAsync($"/api/tasks/{taskId}/complete-setup", null);

        var response = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/add-quantity",
            new { delta = 20 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await adjusterClient.GetAsync($"/api/tasks/{taskId}");
        var body = await taskResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("planQuantity").GetInt32().Should().Be(120);
        body.GetProperty("status").GetString().Should().Be("InProgress");
    }

    /// <summary>
    /// PZP-08: увеличить план у задания не в работе (Issued) нельзя → 400.
    /// </summary>
    [Fact]
    public async Task AddQuantity_NotInProgress_Returns400()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");
        var immId = await _factory.CreateFreshImmAsync();

        var taskId = await CreateTaskAsync(managerClient, immId);
        await IssueTaskAsync(managerClient, taskId); // статус Issued, не InProgress

        var response = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/add-quantity",
            new { delta = 20 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Наладчик завершает задание → статус Completed, код ответа 200.
    /// Полный цикл: Draft → Issue → Start (Setup) → CompleteSetup (InProgress) → Complete.
    /// </summary>
    [Fact]
    public async Task CompleteTask_AsAdjuster_Returns200AndCompletedStatus()
    {
        var managerClient  = await CreateAuthenticatedClientAsync("test_manager",  "Manager123!");
        var adjusterClient = await CreateAuthenticatedClientAsync("test_adjuster", "Adjuster123!");
        var immId = await _factory.CreateFreshImmAsync();

        var taskId = await CreateTaskAsync(managerClient, immId);
        await IssueTaskAsync(managerClient, taskId);
        await adjusterClient.PostAsync($"/api/tasks/{taskId}/start", null);
        await adjusterClient.PostAsync($"/api/tasks/{taskId}/complete-setup", null);

        var completeResponse = await adjusterClient.PostAsJsonAsync(
            $"/api/tasks/{taskId}/complete",
            new { actualQuantity = 100 });

        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var taskResponse = await adjusterClient.GetAsync($"/api/tasks/{taskId}");
        var body = await taskResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("status").GetString().Should().Be("Completed");
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
        body.GetProperty("status").GetString().Should().Be("Closed");
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

    private object MakeCreateRequest(Guid? immId = null) => new
    {
        immId = immId ?? _factory.TestImmId,
        moldId = _factory.TestMoldId,
        planQuantity = 100,
        note = "Integration test task"
    };

    private async Task<Guid> CreateTaskAsync(HttpClient client, Guid? immId = null)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", MakeCreateRequest(immId));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private static async Task IssueTaskAsync(HttpClient client, Guid taskId)
    {
        var response = await client.PostAsync($"/api/tasks/{taskId}/issue", null);
        response.EnsureSuccessStatusCode();
    }
}
