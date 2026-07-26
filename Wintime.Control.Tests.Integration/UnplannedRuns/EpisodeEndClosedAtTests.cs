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

/// <summary>
/// UC-5: конец эпизода Eend = COALESCE(ClosedAt, maxCycleEnd), а не только последний цикл.
/// Регресс-тест на баг: если ТПА остановили задолго до фактического принятия задания,
/// использование last-cycle-end вместо ClosedAt делало разрыв "сразу после" огромным,
/// и задание, реально закрывшее эпизод, исключалось из кандидатов / отклонялось при назначении.
/// </summary>
[Collection("Integration")]
public class EpisodeEndClosedAtTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EpisodeEndClosedAtTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(Guid runId, Guid taskId)> SeedEpisodeClosedLongAfterLastCycleAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        var t0 = DateTime.UtcNow.AddHours(-2);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        // Один сирота-цикл, средняя длительность = 60с → порог "сразу после" = 1.5×60 = 90с.
        // ClosedAt эпизода = t0+600с — сильно дальше последнего цикла (t0+60с).
        var run = new UnplannedRun { ImmId = immId, StartTime = t0, ClosedAt = t0.AddSeconds(600) };
        db.UnplannedRuns.Add(run);
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true,
            StartTime = t0, EndTime = t0.AddSeconds(60), DurationSeconds = 60
        });

        // Задание "забыли принять вовремя": наладка стартовала ровно в момент ClosedAt эпизода.
        // IssuedAt намеренно на 3 дня раньше StartTime эпизода — чтобы исключить попадание
        // в кандидаты по резервному правилу "backdated" (SameDate), а не по ClosedAt-смежности.
        var task = new EntityTask
        {
            ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 50,
            Status = TaskStatus.Setup, IssuedAt = t0.AddDays(-3), SetupStartedAt = t0.AddSeconds(600)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return (run.Id, task.Id);
    }

    [Fact]
    public async Task Candidates_include_task_that_closed_episode_via_ClosedAt()
    {
        var (runId, taskId) = await SeedEpisodeClosedLongAfterLastCycleAsync();
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var candidates = await client.GetFromJsonAsync<List<TaskCandidateDto>>($"/api/unplanned-runs/{runId}/candidates");

        candidates.Should().ContainSingle(c => c.TaskId == taskId);
        candidates!.Single(c => c.TaskId == taskId).Recommended.Should().BeTrue();
    }

    [Fact]
    public async Task Assign_succeeds_for_task_that_closed_episode_via_ClosedAt()
    {
        var (runId, taskId) = await SeedEpisodeClosedLongAfterLastCycleAsync();
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync($"/api/unplanned-runs/{runId}/assign", new AssignTaskRequestDto { TaskId = taskId });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
