using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;

namespace Wintime.Control.Tests.Integration.UnplannedRuns;

[Collection("Integration")]
public class StartTaskClosesEpisodeTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public StartTaskClosesEpisodeTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task StartTask_closes_open_unplanned_run_on_same_imm()
    {
        var immId = await _factory.CreateFreshImmAsync();
        Guid taskId, runId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var task = new EntityTask { ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 100, Status = Wintime.Control.Core.Enums.TaskStatus.Issued, IssuedAt = DateTime.UtcNow };
            db.ShiftTasks.Add(task);
            var run = new UnplannedRun { ImmId = immId, StartTime = DateTime.UtcNow.AddMinutes(-10) };
            db.UnplannedRuns.Add(run);
            await db.SaveChangesAsync();
            taskId = task.Id; runId = run.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.PostAsync($"/api/tasks/{taskId}/start", null);
        resp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var run = await db.UnplannedRuns.FindAsync(runId);
            run!.ClosedAt.Should().NotBeNull();
        }
    }
}
