using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Imm = Wintime.Control.Core.Entities.Imm;
using ImmCycle = Wintime.Control.Core.Entities.ImmCycle;
using Mold = Wintime.Control.Core.Entities.Mold;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class TaskOutputHandlerTests
{
    private readonly IEmulatorControlService _emulator = Substitute.For<IEmulatorControlService>();

    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async SystemTask InProgress_auto_success_increments_actual_quantity_and_material()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 2, PartWeightGrams = 10m, RunnerWeightGrams = 5m };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 1000, Status = EntityTaskStatus.InProgress };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var cycle = new ImmCycle { ImmId = immId, TaskId = task.Id, MoldId = mold.Id, IsSuccessful = true, Cavities = 2, StartTime = DateTime.UtcNow.AddSeconds(-10), EndTime = DateTime.UtcNow };
        db.ImmCycles.Add(cycle);
        await db.SaveChangesAsync();

        var sut = new TaskOutputHandler(db, _emulator);
        await sut.HandleAsync(new CompletedCycle(cycle, task, "auto"));

        var reloaded = await db.ShiftTasks.FindAsync(task.Id);
        reloaded!.ActualQuantity.Should().Be(2);
        reloaded.ActualMaterialWeightGrams.Should().Be(2 * 10m + 5m);
    }

    [Fact]
    public async SystemTask Orphan_cycle_does_not_count_output()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();
        var cycle = new ImmCycle { ImmId = immId, TaskId = null, IsSuccessful = true, Cavities = 0, StartTime = DateTime.UtcNow.AddSeconds(-10), EndTime = DateTime.UtcNow };
        db.ImmCycles.Add(cycle);
        await db.SaveChangesAsync();

        var sut = new TaskOutputHandler(db, _emulator);
        await sut.HandleAsync(new CompletedCycle(cycle, null, "auto")); // не должно бросить
    }
}
