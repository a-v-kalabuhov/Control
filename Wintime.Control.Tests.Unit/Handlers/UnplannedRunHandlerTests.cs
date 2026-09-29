using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Shared.Settings;
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

    private static IOptions<DowntimeSettings> Threshold(int seconds)
        => Options.Create(new DowntimeSettings { IdleThresholdSeconds = seconds });

    [Fact]
    public async SystemTask Opens_episode_on_first_orphan_cycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var end = DateTime.UtcNow;
        var sut = new UnplannedRunHandler(db, Threshold(900));

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
        var sut = new UnplannedRunHandler(db, Threshold(900));

        await sut.HandleAsync(new CompletedCycle(OrphanCycle(immId, DateTime.UtcNow), null, "auto"));

        (await db.UnplannedRuns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async SystemTask Splits_episode_when_gap_since_last_orphan_cycle_exceeds_idle_threshold()
    {
        // Две сессии работы без задания, между ними простой дольше порога (900с) —
        // это два эпизода, а не один длинный.
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var t0 = DateTime.UtcNow.AddHours(-5);
        var oldRun = new UnplannedRun { ImmId = immId, StartTime = t0 };
        db.UnplannedRuns.Add(oldRun);
        db.ImmCycles.Add(OrphanCycle(immId, t0));
        db.ImmCycles.Add(OrphanCycle(immId, t0.AddSeconds(60)));
        var current = OrphanCycle(immId, t0.AddHours(4));
        db.ImmCycles.Add(current); // стадия 1 уже сохранила цикл
        await db.SaveChangesAsync();
        var sut = new UnplannedRunHandler(db, Threshold(900));

        await sut.HandleAsync(new CompletedCycle(current, null, "auto"));

        var runs = await db.UnplannedRuns.OrderBy(r => r.StartTime).ToListAsync();
        runs.Should().HaveCount(2);
        runs[0].ClosedAt.Should().Be(t0.AddSeconds(60)); // конец последнего цикла старого эпизода
        runs[1].StartTime.Should().Be(current.EndTime!.Value);
        runs[1].ClosedAt.Should().BeNull();
    }

    [Fact]
    public async SystemTask Keeps_episode_when_gap_is_within_idle_threshold()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var t0 = DateTime.UtcNow.AddHours(-1);
        db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = t0 });
        db.ImmCycles.Add(OrphanCycle(immId, t0));
        var current = OrphanCycle(immId, t0.AddSeconds(890)); // старт через 880с после конца предыдущего
        db.ImmCycles.Add(current);
        await db.SaveChangesAsync();
        var sut = new UnplannedRunHandler(db, Threshold(900));

        await sut.HandleAsync(new CompletedCycle(current, null, "auto"));

        var run = await db.UnplannedRuns.SingleAsync();
        run.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async SystemTask Ignores_cycle_with_active_task()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var task = new EntityTask { ImmId = immId, Status = EntityTaskStatus.InProgress };
        var cycle = new ImmCycle { ImmId = immId, TaskId = task.Id, EndTime = DateTime.UtcNow };
        var sut = new UnplannedRunHandler(db, Threshold(900));

        await sut.HandleAsync(new CompletedCycle(cycle, task, "auto"));

        (await db.UnplannedRuns.CountAsync()).Should().Be(0);
    }
}
