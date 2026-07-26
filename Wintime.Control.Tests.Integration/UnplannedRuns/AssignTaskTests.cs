using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.UnplannedRun;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

[Collection("Integration")]
public class AssignTaskTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public AssignTaskTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(Guid runId, Guid taskId, Guid immId)> SeedEpisodeWithAdjacentTaskAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var run = new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = start.AddMinutes(2) };
        db.UnplannedRuns.Add(run);
        // 2 сироты-цикла по 2 гнезда, годные
        db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60 });
        db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true, StartTime = start.AddSeconds(60), EndTime = start.AddSeconds(120), DurationSeconds = 60 });
        var task = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, IssuedAt = start, SetupStartedAt = start.AddMinutes(2) };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return (run.Id, task.Id, immId);
    }

    [Fact]
    public async Task Assign_backfills_orphan_cycles_and_recomputes_output()
    {
        var (runId, taskId, immId) = await SeedEpisodeWithAdjacentTaskAsync();
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var cycles = await db.ImmCycles.Where(c => c.ImmId == immId).ToListAsync();
        cycles.Should().OnlyContain(c => c.TaskId == taskId && c.MoldId == _factory.TestMoldId);
        cycles.Should().OnlyContain(c => c.Cavities == 1); // из Mold.Cavities тестовой ПФ (=1)
        var task = await db.ShiftTasks.FindAsync(taskId);
        task!.ActualQuantity.Should().Be(2); // 2 цикла × 1 гнездо
        var run = await db.UnplannedRuns.FindAsync(runId);
        run!.AssignedTaskId.Should().Be(taskId);
        run.AssignedByUserId.Should().NotBeNull();
    }

    [Fact]
    public async Task Assign_rejects_task_of_other_imm()
    {
        var (runId, _, _) = await SeedEpisodeWithAdjacentTaskAsync();
        var otherImm = await _factory.CreateFreshImmAsync();
        Guid otherTaskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var t = new EntityTask { ImmId = otherImm, MoldId = _factory.TestMoldId, PlanQuantity = 10, Status = TaskStatus.Issued, IssuedAt = DateTime.UtcNow };
            db.ShiftTasks.Add(t);
            await db.SaveChangesAsync();
            otherTaskId = t.Id;
        }
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = otherTaskId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reassign_rolls_back_previous_binding()
    {
        var (runId, taskA, immId) = await SeedEpisodeWithAdjacentTaskAsync();
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskA });

        // второе смежное задание того же ТПА
        Guid taskB;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = await db.UnplannedRuns.FindAsync(runId);
            var t = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, IssuedAt = run!.StartTime, SetupStartedAt = run.ClosedAt };
            db.ShiftTasks.Add(t);
            await db.SaveChangesAsync();
            taskB = t.Id;
        }

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskB });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var taskAReloaded = await db.ShiftTasks.FindAsync(taskA);
            taskAReloaded!.ActualQuantity.Should().Be(0, "откат прежней привязки уменьшил выпуск A");
            var taskBReloaded = await db.ShiftTasks.FindAsync(taskB);
            taskBReloaded!.ActualQuantity.Should().Be(2);
        }
    }
}
