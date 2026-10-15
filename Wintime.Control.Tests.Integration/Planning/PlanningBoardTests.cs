using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Planning;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-07a: доска план-графика цеха — отбор заданий по окну, строки ТПА, роли.</summary>
[Collection("Integration")]
public class PlanningBoardTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PlanningBoardTests(IntegrationTestFactory factory) => _factory = factory;

    // Enum-статусы приходят строками (JsonStringEnumConverter в Program.cs)
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task<HttpClient> ClientAsync(string login = "test_manager", string password = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string BoardUrl(DateTime from, DateTime to) =>
        $"/api/planning/board?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

    private async Task<PlanningBoardDto> GetBoardAsync(DateTime from, DateTime to)
    {
        var client = await ClientAsync();
        var response = await client.GetAsync(BoardUrl(from, to));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PlanningBoardDto>(JsonOptions))!;
    }

    private async Task<Guid> SeedMoldAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-07a", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true,
            ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    // План 100 шт, 2 гнезда, эталон 60 с → выпуск 50 × 60 = 3000 с; наладка 30 мин; буфер 0
    private async Task<Guid> SeedTaskAsync(Guid immId, Guid moldId, Action<ShiftTask> setup)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, PlanQuantity = 100, PlannedFullCycleSeconds = 60,
            PlannedSetupMinutes = 30, Status = TaskStatus.Planned,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow), PlannedSetupStartTime = new TimeOnly(10, 0)
        };
        setup(task);
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task ArchiveImmAsync(Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = await db.Imms.SingleAsync(i => i.Id == immId);
        imm.IsActive = false;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Planned_task_in_window_is_on_board_with_plan_segments()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var moldId = await SeedMoldAsync();
        var taskId = await SeedTaskAsync(immId, moldId,
            t => t.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));

        var board = await GetBoardAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));

        var row = board.Rows.Single(r => r.Imm.Id == immId);
        row.Imm.IsActive.Should().BeTrue();
        var item = row.Tasks.Single(t => t.Task.Id == taskId);
        (item.Plan.SetupEnd - item.Plan.SetupStart).Should().Be(TimeSpan.FromMinutes(30));
        (item.Plan.ReleaseEnd!.Value - item.Plan.SetupEnd).Should().Be(TimeSpan.FromSeconds(3000));
        item.Plan.BufferEnd.Should().Be(item.Task.PlannedEndAt);
        item.ProductArticle.Should().NotBeNullOrEmpty();
        item.OverdueNotStartedSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Task_planned_beyond_window_is_not_on_board()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var moldId = await SeedMoldAsync();
        await SeedTaskAsync(immId, moldId,
            t => t.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20));

        var board = await GetBoardAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));

        board.Rows.Single(r => r.Imm.Id == immId).Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task Planned_task_passing_date_prefilter_but_ending_before_window_is_not_on_board()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var moldId = await SeedMoldAsync();
        var taskId = await SeedTaskAsync(immId, moldId, _ => { });
        var wide = await GetBoardAsync(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(3));
        var plan = wide.Rows.Single(r => r.Imm.Id == immId).Tasks.Single(t => t.Task.Id == taskId).Plan;
        var from = plan.BufferEnd!.Value.AddHours(1);

        var board = await GetBoardAsync(from, from.AddDays(1));

        board.Rows.Single(r => r.Imm.Id == immId).Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task InProgress_task_planned_before_window_is_on_board_by_activity_with_forecast()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var moldId = await SeedMoldAsync();
        var started = DateTime.UtcNow.AddDays(-5);
        var taskId = await SeedTaskAsync(immId, moldId, t =>
        {
            t.PlannedDate = DateOnly.FromDateTime(started);
            t.Status = TaskStatus.InProgress;
            t.SetupStartedAt = started;
            t.StartedAt = started.AddHours(1);
            t.ActualQuantity = 10;
        });

        var board = await GetBoardAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));

        var item = board.Rows.Single(r => r.Imm.Id == immId).Tasks.Single(t => t.Task.Id == taskId);
        // Циклов нет → эталон: остаток 90 шт = 45 смыканий × 60 с
        item.Fact.ForecastEnd.Should().NotBeNull();
        item.Fact.ForecastEnd!.Value.Should().BeCloseTo(DateTime.UtcNow.AddSeconds(2700), TimeSpan.FromSeconds(60));
        item.BufferRemainingSeconds.Should().BeNegative(); // плановое окончание 5 суток назад
    }

    [Fact]
    public async Task Archived_imm_is_shown_only_with_tasks_in_window()
    {
        var withTask = await _factory.CreateFreshImmAsync();
        var withoutTask = await _factory.CreateFreshImmAsync();
        var moldId = await SeedMoldAsync();
        await SeedTaskAsync(withTask, moldId, _ => { });
        await ArchiveImmAsync(withTask);
        await ArchiveImmAsync(withoutTask);

        var board = await GetBoardAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(6));

        board.Rows.Single(r => r.Imm.Id == withTask).Imm.IsActive.Should().BeFalse();
        board.Rows.Should().NotContain(r => r.Imm.Id == withoutTask);
    }

    [Fact]
    public async Task Observer_gets_board()
    {
        var client = await ClientAsync("test_observer", "Observer123!");

        var response = await client.GetAsync(BoardUrl(DateTime.UtcNow, DateTime.UtcNow.AddDays(7)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Adjuster_is_forbidden()
    {
        var client = await ClientAsync("test_adjuster", "Adjuster123!");

        var response = await client.GetAsync(BoardUrl(DateTime.UtcNow, DateTime.UtcNow.AddDays(7)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(0)]   // to == from
    [InlineData(63)]  // длиннее 62 суток
    public async Task Invalid_window_is_bad_request(int days)
    {
        var client = await ClientAsync();
        var from = DateTime.UtcNow;

        var response = await client.GetAsync(BoardUrl(from, from.AddDays(days)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
