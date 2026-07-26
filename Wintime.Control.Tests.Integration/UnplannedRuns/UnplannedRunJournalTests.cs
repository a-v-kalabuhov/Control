using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.UnplannedRun;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

[Collection("Integration")]
public class UnplannedRunJournalTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public UnplannedRunJournalTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task List_returns_derived_cycle_count_and_end()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = start });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start.AddSeconds(60), EndTime = start.AddSeconds(120), DurationSeconds = 60, IsSuccessful = true });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var list = await client.GetFromJsonAsync<List<UnplannedRunDto>>($"/api/unplanned-runs?immId={immId}");

        var run = list!.Single();
        run.CycleCount.Should().Be(2);
        run.AvgCycleDuration.Should().Be(60);
        // BeCloseTo (не Be): PostgreSQL хранит timestamptz с точностью до микросекунд,
        // .NET DateTime — до 100нс тиков; при обратном чтении из БД последний разряд может округляться.
        run.EndTime.Should().BeCloseTo(start.AddSeconds(120), TimeSpan.FromMicroseconds(1));
    }

    [Fact]
    public async Task Candidates_returns_only_same_imm_adjacent_tasks()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var otherImm = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        Guid runId, adjacentTaskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = start.AddMinutes(2) };
            db.UnplannedRuns.Add(run);
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });

            // задание того же ТПА, начавшееся сразу после эпизода (Setup)
            var adj = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, IssuedAt = start, SetupStartedAt = start.AddMinutes(2) };
            db.ShiftTasks.Add(adj);
            // задание ДРУГОГО ТПА — не должно попасть
            db.ShiftTasks.Add(new EntityTask { ImmId = otherImm, MoldId = _factory.TestMoldId, PlanQuantity = 50, Status = TaskStatus.Setup, SetupStartedAt = start.AddMinutes(2) });
            await db.SaveChangesAsync();
            runId = run.Id; adjacentTaskId = adj.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var candidates = await client.GetFromJsonAsync<List<TaskCandidateDto>>($"/api/unplanned-runs/{runId}/candidates");

        candidates!.Select(c => c.TaskId).Should().ContainSingle().Which.Should().Be(adjacentTaskId);
        candidates.Single().Recommended.Should().BeTrue();
    }

    [Fact]
    public async Task Candidates_excludes_task_separated_by_a_real_time_gap()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        Guid runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = start.AddMinutes(2) };
            db.UnplannedRuns.Add(run);
            // средняя длительность цикла эпизода — 60с, порог смежности = 1.5 * 60 = 90с
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = start.AddSeconds(60), DurationSeconds = 60, IsSuccessful = true });

            // задание того же ТПА, но с циклами, закончившимися задолго (3 часа) ДО эпизода —
            // разрыв реальный (не "сразу перед"), задание уже имеет циклы, поэтому правило (d)
            // "нет циклов вовсе + та же дата" тут неприменимо — не кандидат.
            var farTask = new EntityTask
            {
                ImmId = immId,
                MoldId = _factory.TestMoldId,
                PlanQuantity = 50,
                Status = TaskStatus.Completed,
                IssuedAt = start.AddHours(-4),
                SetupStartedAt = start.AddHours(-4),
                StartedAt = start.AddHours(-4),
                CompletedAt = start.AddHours(-3)
            };
            db.ShiftTasks.Add(farTask);
            await db.SaveChangesAsync();
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = farTask.Id, StartTime = start.AddHours(-3.1), EndTime = start.AddHours(-3), DurationSeconds = 60, IsSuccessful = true });
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var candidates = await client.GetFromJsonAsync<List<TaskCandidateDto>>($"/api/unplanned-runs/{runId}/candidates");

        candidates!.Should().BeEmpty();
    }

    [Fact]
    public async Task Candidates_recommends_overlap_over_after_over_before()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var start = DateTime.UtcNow.AddMinutes(-20);
        var episodeEnd = start.AddSeconds(60);
        Guid runId, overlapTaskId, afterTaskId, beforeTaskId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = new UnplannedRun { ImmId = immId, StartTime = start, ClosedAt = episodeEnd };
            db.UnplannedRuns.Add(run);
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = null, StartTime = start, EndTime = episodeEnd, DurationSeconds = 60, IsSuccessful = true });

            // "before" task: закончилось непосредственно перед эпизодом (за 30с до start)
            var beforeTask = new EntityTask
            {
                ImmId = immId,
                MoldId = _factory.TestMoldId,
                PlanQuantity = 10,
                Status = TaskStatus.Completed,
                IssuedAt = start.AddMinutes(-30),
                SetupStartedAt = start.AddMinutes(-30),
                StartedAt = start.AddMinutes(-29)
            };
            db.ShiftTasks.Add(beforeTask);

            // "after" task: началось сразу после эпизода (30с после episodeEnd)
            var afterTask = new EntityTask
            {
                ImmId = immId,
                MoldId = _factory.TestMoldId,
                PlanQuantity = 20,
                Status = TaskStatus.Setup,
                IssuedAt = start,
                SetupStartedAt = episodeEnd.AddSeconds(30)
            };
            db.ShiftTasks.Add(afterTask);

            // "overlap" task: рабочий интервал (SetupStartedAt..CompletedAt) охватывает окно эпизода
            var overlapTask = new EntityTask
            {
                ImmId = immId,
                MoldId = _factory.TestMoldId,
                PlanQuantity = 30,
                Status = TaskStatus.Completed,
                IssuedAt = start.AddMinutes(-5),
                SetupStartedAt = start.AddMinutes(-5),
                StartedAt = start.AddMinutes(-5),
                CompletedAt = episodeEnd.AddMinutes(5)
            };
            db.ShiftTasks.Add(overlapTask);

            await db.SaveChangesAsync();
            runId = run.Id;
            beforeTaskId = beforeTask.Id;
            afterTaskId = afterTask.Id;
            overlapTaskId = overlapTask.Id;

            // цикл для "before" задания, заканчивающийся непосредственно перед эпизодом
            db.ImmCycles.Add(new ImmCycle { ImmId = immId, TaskId = beforeTaskId, StartTime = start.AddSeconds(-90), EndTime = start.AddSeconds(-30), DurationSeconds = 60, IsSuccessful = true });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var candidates = await client.GetFromJsonAsync<List<TaskCandidateDto>>($"/api/unplanned-runs/{runId}/candidates");

        candidates!.Select(c => c.TaskId).Should().Contain(new[] { overlapTaskId, afterTaskId, beforeTaskId });
        var recommended = candidates.Single(c => c.Recommended);
        recommended.TaskId.Should().Be(overlapTaskId);
    }
}
