using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Planning;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-07b: GET /api/planning/conflicts — пары по ТПА и ПФ, порог, роли, ничего не блокирует.</summary>
[Collection("Integration")]
public class PlanConflictsApiTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PlanConflictsApiTests(IntegrationTestFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Через 5 суток — далеко от «сейчас», задания не просрочены
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);

    private async Task<HttpClient> ClientAsync(string login = "test_manager", string password = "Manager123!")
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<PlanConflictsDto> GetConflictsAsync()
    {
        var client = await ClientAsync();
        var resp = await client.GetAsync("/api/planning/conflicts");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<PlanConflictsDto>(JsonOptions))!;
    }

    private async Task<Guid> SeedMoldAsync(int cavities = 2)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = $"M-{Guid.NewGuid():N}"[..10], FormId = $"FORM-{Guid.NewGuid():N}", Cavities = cavities,
            IsActive = true, ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    // План 100 шт, 2 гнезда, эталон 60 с → выпуск 50 мин; наладка 30 мин; буфер 0 → окно 80 мин
    private async Task<Guid> SeedTaskAsync(Guid immId, Guid moldId, TimeOnly time, Action<ShiftTask>? setup = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, ProductId = _factory.TestProductId, PlanQuantity = 100,
            PlannedFullCycleSeconds = 60, PlannedSetupMinutes = 30, Status = TaskStatus.Planned,
            PlannedDate = Day, PlannedSetupStartTime = time
        };
        setup?.Invoke(task);
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private static IEnumerable<PlanConflictDto> Between(PlanConflictsDto r, Guid a, Guid b) =>
        r.Items.Where(i => (i.Task.Id == a && i.Other.Id == b) || (i.Task.Id == b && i.Other.Id == a));

    [Fact]
    public async Task Overlapping_plans_on_same_imm_are_plan_conflict()
    {
        var imm = await _factory.CreateFreshImmAsync();
        var a = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(10, 0));
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(10, 30));   // пересечение 50 мин

        var r = await GetConflictsAsync();

        var c = Between(r, a, b).Should().ContainSingle().Subject;
        c.Kind.Should().Be(ConflictKind.Plan);
        c.Resource.Should().Be(ConflictResource.Imm);
        c.OverlapSeconds.Should().Be(50 * 60);
        c.Task.Id.Should().Be(a);
        c.Task.ImmName.Should().NotBeNullOrEmpty();
        r.ThresholdMinutes.Should().Be(20);
        r.PlanCount.Should().BeGreaterThan(0);
        r.TaskCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Same_mold_on_two_imms_is_mold_conflict()
    {
        var mold = await SeedMoldAsync();
        var a = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, new TimeOnly(14, 0));
        var b = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, new TimeOnly(14, 30));

        var r = await GetConflictsAsync();

        Between(r, a, b).Should().ContainSingle().Which.Resource.Should().Be(ConflictResource.Mold);
    }

    [Fact]
    public async Task Overlap_within_threshold_is_not_conflict_and_cavity_change_creates_it()
    {
        var imm = await _factory.CreateFreshImmAsync();
        var mold = await SeedMoldAsync();
        var a = await SeedTaskAsync(imm, mold, new TimeOnly(18, 0));               // 18:00–19:20
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(19, 0)); // пересечение 20 мин = порог

        Between(await GetConflictsAsync(), a, b).Should().BeEmpty();

        // 1 гнездо → выпуск 100 мин, A кончается 20:10 → пересечение 70 мин
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            (await db.Molds.SingleAsync(m => m.Id == mold)).Cavities = 1;
            await db.SaveChangesAsync();
        }

        Between(await GetConflictsAsync(), a, b).Should().ContainSingle();
    }

    [Fact]
    public async Task Late_issued_task_against_running_task_is_deviation_conflict()
    {
        var imm = await _factory.CreateFreshImmAsync();
        // Идёт выпуск: циклов нет → прогноз по эталону [now, now + 50 мин)
        var running = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(0, 0), t =>
        {
            t.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            t.Status = TaskStatus.InProgress;
            t.SetupStartedAt = DateTime.UtcNow.AddHours(-2);
            t.StartedAt = DateTime.UtcNow.AddHours(-1);
        });
        // Выдано вчера, не начато → прогноз [now + 10, now + 90 мин) — пересечение 40 мин
        var late = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(1, 0), t =>
        {
            t.PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            t.Status = TaskStatus.Issued;
            t.PersonnelId = _factory.TestAdjusterId;
        });

        var r = await GetConflictsAsync();

        Between(r, running, late).Should().ContainSingle().Which.Kind.Should().Be(ConflictKind.Deviation);
        r.DeviationCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Finished_tasks_do_not_participate()
    {
        var imm = await _factory.CreateFreshImmAsync();
        var a = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(6, 0), t => t.Status = TaskStatus.Completed);
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(6, 0));

        Between(await GetConflictsAsync(), a, b).Should().BeEmpty();
    }

    [Fact]
    public async Task Task_in_plan_conflict_can_still_be_issued()
    {
        var imm = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(12, 0));
        var b = await SeedTaskAsync(imm, await SeedMoldAsync(), new TimeOnly(12, 30));
        var client = await ClientAsync();

        var resp = await client.PostAsJsonAsync($"/api/tasks/{b}/issue", new { personnelId = _factory.TestAdjusterId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Observer_can_read_conflicts_adjuster_cannot()
    {
        (await (await ClientAsync("test_observer", "Observer123!")).GetAsync("/api/planning/conflicts"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await ClientAsync("test_adjuster", "Adjuster123!")).GetAsync("/api/planning/conflicts"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
