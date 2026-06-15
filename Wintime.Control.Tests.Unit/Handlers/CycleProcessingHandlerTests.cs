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

namespace Wintime.Control.Tests.Unit.Handlers;

public class CycleProcessingHandlerTests
{
    private readonly ICycleTracker _tracker = Substitute.For<ICycleTracker>();
    private readonly IEmulatorControlService _emulator = Substitute.For<IEmulatorControlService>();

    private static ControlDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ControlDbContext(options);
    }

    private static (Mold mold, EntityTask task) SeedTask(
        ControlDbContext db,
        Guid immId,
        int cavities = 2,
        decimal partWeight = 10m,
        decimal runnerWeight = 5m,
        int planQuantity = 1000)
    {
        var mold = new Mold
        {
            Name = "Test Mold",
            FormId = Guid.NewGuid().ToString(),
            Cavities = cavities,
            PartWeightGrams = partWeight,
            RunnerWeightGrams = runnerWeight,
            MaxResourceCycles = 100_000
        };
        db.Molds.Add(mold);

        var imm = new Imm { Id = immId, Name = "Test IMM", IsActive = true };
        db.Imms.Add(imm);

        var task = new EntityTask
        {
            ImmId = immId,
            MoldId = mold.Id,
            Mold = mold,
            Imm = imm,
            PlanQuantity = planQuantity,
            Status = EntityTaskStatus.InProgress
        };
        db.Tasks.Add(task);
        db.SaveChanges();
        return (mold, task);
    }

    private static MqttProcessingContext MakeCycleContext(Guid immId, int counter, string mode)
    {
        var sensor = PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter");
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string> { ["counter"] = counter.ToString() },
            mode: mode);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
    }

    private static CycleState ActiveCycleState(int lastCounter, string lastMode = "auto")
        => new(DateTime.UtcNow.AddSeconds(-30), lastCounter, lastMode);

    [Fact]
    public async Task SuccessfulCycle_AccumulatesMaterial()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        var (_, task) = SeedTask(db, immId, cavities: 2, partWeight: 10m, runnerWeight: 5m);
        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4));
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "auto"));

        db.Tasks.Find(task.Id)!.ActualMaterialWeightGrams.Should().Be(25m); // 2*10 + 5
    }

    [Fact]
    public async Task SuccessfulCycle_MultipleCycles_AccumulatesCorrectly()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        var (_, task) = SeedTask(db, immId, cavities: 2, partWeight: 10m, runnerWeight: 5m);
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4));
        await sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "auto"));

        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 5));
        await sut.ProcessAsync(MakeCycleContext(immId, counter: 6, mode: "auto"));

        db.Tasks.Find(task.Id)!.ActualMaterialWeightGrams.Should().Be(50m); // 2 cycles × 25
    }

    [Fact]
    public async Task AlarmCycle_DoesNotAccumulateMaterial()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        var (_, task) = SeedTask(db, immId);
        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4));
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "alarm"));

        db.Tasks.Find(task.Id)!.ActualMaterialWeightGrams.Should().Be(0m);
    }

    [Fact]
    public async Task FirstMessage_AutoMode_IsCaseInsensitive_StartsCycle()
    {
        // Коннектор может прислать "AUTO" вместо "auto" — цикл всё равно должен стартовать.
        var immId = Guid.NewGuid();
        var db = CreateDb();
        _tracker.Get(immId).Returns((CycleState?)null);
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, counter: 1, mode: "AUTO"));

        _tracker.Received().Set(immId, Arg.Is<CycleState>(s => s.CycleStartTime != null));
    }

    [Fact]
    public async Task AlarmCycle_IsCaseInsensitive_DoesNotAccumulateMaterial()
    {
        // "ALARM" в верхнем регистре должен трактоваться как авария — цикл не успешен.
        var immId = Guid.NewGuid();
        var db = CreateDb();
        var (_, task) = SeedTask(db, immId);
        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4));
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "ALARM"));

        db.Tasks.Find(task.Id)!.ActualMaterialWeightGrams.Should().Be(0m);
    }

    [Fact]
    public async Task SuccessfulCycle_NoActiveTask_DoesNotThrow()
    {
        var immId = Guid.NewGuid();
        var db = CreateDb();
        _tracker.Get(immId).Returns(ActiveCycleState(lastCounter: 4));
        var sut = new CycleProcessingHandler(db, _tracker, _emulator, NullLogger<CycleProcessingHandler>.Instance);

        var act = () => sut.ProcessAsync(MakeCycleContext(immId, counter: 5, mode: "auto"));

        await act.Should().NotThrowAsync();
    }
}
