using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Auth;

[Collection("Integration")]
public class OperatorDashboardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OperatorDashboardTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> OperatorClientAsync()
    {
        var login = $"operator_{Guid.NewGuid():N}";
        const string password = "Operator123!";
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var result = await userManager.CreateAsync(new User
            {
                UserName = login,
                Email = $"{login}@test.local",
                FullName = "Литейщик Тестовый",
                Role = UserRole.Operator,
                IsActive = true
            }, password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    [Theory]
    [InlineData("/api/imm", HttpStatusCode.OK)]
    [InlineData("/api/dashboard/shift-utilization?from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z", HttpStatusCode.OK)]
    [InlineData("/api/shifts", HttpStatusCode.OK)]
    [InlineData("/api/auth/me", HttpStatusCode.OK)]
    public async Task Operator_CanReadDashboardData(string url, HttpStatusCode expected)
    {
        var client = await OperatorClientAsync();

        var response = await client.GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Operator_CanReadImmCardData()
    {
        var client = await OperatorClientAsync();
        var immId = _factory.TestImmId;
        var taskId = Guid.NewGuid();

        var responses = await Task.WhenAll(
            client.GetAsync($"/api/imm/{immId}"),
            client.GetAsync($"/api/imm/{immId}/status"),
            client.GetAsync($"/api/dashboard/imm-card/{immId}"),
            client.GetAsync($"/api/dashboard/task-results/{taskId}"));

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[2].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, responses[3].StatusCode);
    }

    [Theory]
    [InlineData("/api/orders")]
    [InlineData("/api/personnel")]
    [InlineData("/api/planning/conflicts")]
    [InlineData("/api/reports/daily?date=2026-01-01")]
    [InlineData("/api/tasks?status=Closed")]
    [InlineData("/api/tasks/00000000-0000-0000-0000-000000000000")]
    [InlineData("/api/tasks/00000000-0000-0000-0000-000000000000/results")]
    [InlineData("/api/imm/00000000-0000-0000-0000-000000000000/effective-status-history?from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z")]
    public async Task Operator_CannotReadOtherModules(string url)
    {
        var client = await OperatorClientAsync();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_CannotGetTaskOutsideCurrentShift()
    {
        var completedAt = DateTime.UtcNow.AddYears(-2);
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = new ShiftTask
            {
                ImmId = _factory.TestImmId,
                MoldId = _factory.TestMoldId,
                ProductId = _factory.TestProductId,
                PlanQuantity = 1,
                Cavities = 1,
                PlannedFullCycleSeconds = 60,
                PlannedDate = DateOnly.FromDateTime(completedAt),
                Status = Core.Enums.TaskStatus.Completed,
                SetupStartedAt = completedAt.AddHours(-2),
                StartedAt = completedAt.AddHours(-1),
                CompletedAt = completedAt
            };
            db.ShiftTasks.Add(task);
            await db.SaveChangesAsync();
            taskId = task.Id;
        }
        var client = await OperatorClientAsync();

        var card = await client.GetAsync($"/api/dashboard/imm-card/{_factory.TestImmId}");
        var results = await client.GetAsync($"/api/dashboard/task-results/{taskId}");

        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        Assert.DoesNotContain(taskId.ToString(), await card.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, results.StatusCode);
    }

    [Fact]
    public async Task Operator_CannotGetResultsForArchivedImm()
    {
        Guid taskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var now = DateTime.UtcNow;
            var task = new ShiftTask
            {
                ImmId = _factory.TestImmId,
                MoldId = _factory.TestMoldId,
                ProductId = _factory.TestProductId,
                PlanQuantity = 1,
                Cavities = 1,
                PlannedFullCycleSeconds = 60,
                PlannedDate = DateOnly.FromDateTime(now.AddDays(-1)),
                Status = Core.Enums.TaskStatus.Completed,
                SetupStartedAt = now.AddDays(-1),
                StartedAt = now.AddDays(-1),
                CompletedAt = now
            };
            db.ShiftTasks.Add(task);
            var imm = await db.Imms.FindAsync(_factory.TestImmId);
            imm!.IsActive = false;
            await db.SaveChangesAsync();
            taskId = task.Id;
        }

        try
        {
            var client = await OperatorClientAsync();
            var response = await client.GetAsync($"/api/dashboard/task-results/{taskId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = await db.Imms.FindAsync(_factory.TestImmId);
            imm!.IsActive = true;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Operator_CannotUseTelemetryOrMutateData()
    {
        var client = await OperatorClientAsync();

        var telemetry = await client.GetAsync(
            $"/api/imm/{_factory.TestImmId}/telemetry-dashboard?from=2026-01-01T00:00:00Z&to=2026-01-01T01:00:00Z");
        var saveShifts = await client.PutAsJsonAsync("/api/shifts", new { shifts = Array.Empty<object>() });
        var createTask = await client.PostAsJsonAsync("/api/tasks", new { });

        Assert.Equal(HttpStatusCode.Forbidden, telemetry.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, saveShifts.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createTask.StatusCode);
    }
}
