using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Imm = Wintime.Control.Core.Entities.Imm;
using Mold = Wintime.Control.Core.Entities.Mold;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class CycleProcessingHandlerTests
{
    private readonly ICycleTracker _tracker = Substitute.For<ICycleTracker>();

    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class ThrowingHandler : ICycleHandler
    {
        public bool Called { get; private set; }
        public SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class SpyHandler : ICycleHandler
    {
        public bool Called { get; private set; }
        public SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default)
        {
            Called = true;
            return SystemTask.CompletedTask;
        }
    }

    private static MqttProcessingContext MakeCycleContext(Guid immId, int counter, string mode)
    {
        var sensor = PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter");
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string> { ["counter"] = counter.ToString() }, mode: mode);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
    }

    [Fact]
    public async SystemTask Persists_orphan_cycle_and_runs_all_handlers_even_when_one_throws()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        // активный цикл в трекере: auto, счётчик 5 → приходит 6 → цикл завершён
        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var throwing = new ThrowingHandler();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, _tracker, [throwing, spy], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));

        var cycle = await db.ImmCycles.SingleOrDefaultAsync();
        cycle.Should().NotBeNull("цикл сохранён на Стадии 1 до конвейера");
        cycle!.TaskId.Should().BeNull("нет активного задания → сирота");
        throwing.Called.Should().BeTrue();
        spy.Called.Should().BeTrue("сбой одного хендлера не прерывает конвейер");
    }

    [Fact]
    public async SystemTask Setup_task_gates_cycle_write_even_with_active_tracker_cycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 4 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.Setup };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        // трекер уже держит "активный" цикл (auto, счётчик 5) — наладка всё равно должна гасить запись
        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));

        (await db.ImmCycles.CountAsync()).Should().Be(0, "Setup (наладка) гасит запись цикла — ShouldProcessCycle=false");
    }

    [Fact]
    public async SystemTask InProgress_cycle_snapshots_cavities_from_task_mold()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 4 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.InProgress };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.Cavities.Should().Be(4, "снапшот из Mold.Cavities активного задания на момент цикла");
    }

    [Fact]
    public async SystemTask Uppercase_ALARM_mode_change_ends_cycle_as_unsuccessful()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 2 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.InProgress };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        // активный цикл: последний режим auto, счётчик не изменится — цикл завершается по смене режима
        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, 5, "ALARM"));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.IsSuccessful.Should().BeFalse("режим ALARM (в любом регистре) нормализуется в alarm → цикл неуспешен");
    }
}
