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
public class TaskListFiltersApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Окно запроса: [W0, W1)
    private static readonly DateTime W0 = new(2025, 6, 10, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime W1 = W0.AddHours(12);

    public TaskListFiltersApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(Guid immId, Guid moldId)> CreateImmAndMoldAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold { Name = $"M-{Guid.NewGuid():N}", FormId = $"F-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return (immId, mold.Id);
    }

    private async Task<Guid> AddTaskAsync(Guid immId, Guid moldId, TaskStatus status, DateOnly plannedDate,
        DateTime? issued = null, DateTime? setup = null, DateTime? started = null,
        DateTime? completed = null, DateTime? closed = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, PlanQuantity = 10, Status = status, PlannedDate = plannedDate,
            IssuedAt = issued, SetupStartedAt = setup, StartedAt = started, CompletedAt = completed, ClosedAt = closed
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<List<Guid>> GetIdsAsync(string query)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.GetAsync($"/api/tasks?{query}");
        resp.EnsureSuccessStatusCode();
        var items = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return items.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
    }

    private static string Iso(DateTime d) => Uri.EscapeDataString(d.ToString("O"));

    [Fact]
    public async Task Active_ReturnsTasksOverlappingWindow_IncludingCompleted()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var spanning = await AddTaskAsync(imm, mold, TaskStatus.InProgress, day.AddDays(-2),
            issued: W0.AddDays(-2), setup: W0.AddDays(-2), started: W0.AddDays(-2).AddHours(1));
        var completedInside = await AddTaskAsync(imm, mold, TaskStatus.Completed, day,
            issued: W0, setup: W0.AddHours(1), started: W0.AddHours(2), completed: W0.AddHours(5));
        var issuedNotStarted = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddHours(1));
        var finishedBefore = await AddTaskAsync(imm, mold, TaskStatus.Completed, day.AddDays(-3),
            issued: W0.AddDays(-3), setup: W0.AddDays(-3), started: W0.AddDays(-3).AddHours(1), completed: W0.AddHours(-1));

        var ids = await GetIdsAsync($"immId={imm}&activeFrom={Iso(W0)}&activeTo={Iso(W1)}");

        ids.Should().BeEquivalentTo(new[] { spanning, completedInside });
        ids.Should().NotContain(issuedNotStarted).And.NotContain(finishedBefore);
    }

    [Fact]
    public async Task Issued_FiltersByIssuedAt_ExcludesPlannedAndArchiveByDefault()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var issuedInside = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddHours(1));
        var planned = await AddTaskAsync(imm, mold, TaskStatus.Planned, day);
        var issuedOutside = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddDays(-5));
        var completedInside = await AddTaskAsync(imm, mold, TaskStatus.Completed, day,
            issued: W0.AddHours(1), setup: W0.AddHours(2), started: W0.AddHours(3), completed: W0.AddHours(4));

        var ids = await GetIdsAsync($"immId={imm}&issuedFrom={Iso(W0)}&issuedTo={Iso(W1)}");

        ids.Should().BeEquivalentTo(new[] { issuedInside });
        ids.Should().NotContain(planned).And.NotContain(issuedOutside).And.NotContain(completedInside);
    }

    [Fact]
    public async Task Planned_FiltersByPlannedDate()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var onDay = await AddTaskAsync(imm, mold, TaskStatus.Planned, day);
        var nextDay = await AddTaskAsync(imm, mold, TaskStatus.Planned, day.AddDays(1));
        var before = await AddTaskAsync(imm, mold, TaskStatus.Planned, day.AddDays(-1));

        var ids = await GetIdsAsync($"immId={imm}&plannedFrom={day:yyyy-MM-dd}&plannedTo={day.AddDays(1):yyyy-MM-dd}");

        ids.Should().BeEquivalentTo(new[] { onDay, nextDay });
        ids.Should().NotContain(before);
    }

    [Fact]
    public async Task Filters_CombineWithAnd()
    {
        var (imm, mold) = await CreateImmAndMoldAsync();
        var day = DateOnly.FromDateTime(W0);
        var both = await AddTaskAsync(imm, mold, TaskStatus.Issued, day, issued: W0.AddHours(1));
        var onlyIssued = await AddTaskAsync(imm, mold, TaskStatus.Issued, day.AddDays(7), issued: W0.AddHours(1));

        var ids = await GetIdsAsync(
            $"immId={imm}&issuedFrom={Iso(W0)}&issuedTo={Iso(W1)}&plannedFrom={day:yyyy-MM-dd}&plannedTo={day:yyyy-MM-dd}");

        ids.Should().BeEquivalentTo(new[] { both });
        ids.Should().NotContain(onlyIssued);
    }
}
