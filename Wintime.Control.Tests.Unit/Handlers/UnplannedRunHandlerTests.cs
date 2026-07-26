using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class UnplannedRunHandlerTests
{
    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ImmCycle OrphanCycle(Guid immId, DateTime end)
        => new() { ImmId = immId, TaskId = null, Cavities = 0, IsSuccessful = true, StartTime = end.AddSeconds(-10), EndTime = end };

    [Fact]
    public async SystemTask Opens_episode_on_first_orphan_cycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var end = DateTime.UtcNow;
        var sut = new UnplannedRunHandler(db);

        await sut.HandleAsync(new CompletedCycle(OrphanCycle(immId, end), null, "auto"));

        var run = await db.UnplannedRuns.SingleOrDefaultAsync();
        run.Should().NotBeNull();
        run!.ImmId.Should().Be(immId);
        run.StartTime.Should().Be(end);
        run.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async SystemTask Does_not_duplicate_when_open_episode_exists()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = DateTime.UtcNow.AddMinutes(-5) });
        await db.SaveChangesAsync();
        var sut = new UnplannedRunHandler(db);

        await sut.HandleAsync(new CompletedCycle(OrphanCycle(immId, DateTime.UtcNow), null, "auto"));

        (await db.UnplannedRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async SystemTask Ignores_cycle_with_active_task()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var task = new EntityTask { ImmId = immId, Status = EntityTaskStatus.InProgress };
        var cycle = new ImmCycle { ImmId = immId, TaskId = task.Id, EndTime = DateTime.UtcNow };
        var sut = new UnplannedRunHandler(db);

        await sut.HandleAsync(new CompletedCycle(cycle, task, "auto"));

        (await db.UnplannedRuns.CountAsync()).Should().Be(0);
    }
}
