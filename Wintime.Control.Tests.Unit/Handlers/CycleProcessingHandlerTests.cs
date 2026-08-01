using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Enums;
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

    /// <summary>
    /// Контекст с тремя сенсорами: счётчик + защёлкнутые длительности цикла литья и паузы.
    /// </summary>
    private static MqttProcessingContext MakeCycleContextWithDurations(
        Guid immId, int counter, string mode, string injectionMs, string pauseMs)
    {
        var sensors = new[]
        {
            PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter"),
            PipelineTestFixtures.MakeSensor("inj", type: "injectionDuration"),
            PipelineTestFixtures.MakeSensor("pause", type: "cyclePause")
        };
        var template = PipelineTestFixtures.MakeTemplate(sensors);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string>
            {
                ["counter"] = counter.ToString(),
                ["inj"] = injectionMs,
                ["pause"] = pauseMs
            }, mode: mode);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}",
            data: message, device: device, template: template);
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

    [Fact]
    public async SystemTask Completed_cycle_stores_injection_duration_and_pause()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        // Значения приходят защёлкнутыми в сообщении, закрывающем цикл.
        await sut.ProcessAsync(MakeCycleContextWithDurations(immId, 6, "auto", "12500", "3400"));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.InjectionDurationMs.Should().Be(12500);
        cycle.PauseDurationMs.Should().Be(3400);
    }

    [Fact]
    public async SystemTask Completed_cycle_without_duration_sensors_leaves_fields_null()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        _tracker.Get(immId).Returns(new CycleState(DateTime.UtcNow.AddSeconds(-20), 5, "auto"));

        var sut = new CycleProcessingHandler(db, _tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        // MakeCycleContext даёт шаблон только со счётчиком — машина без сигналов формы.
        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.InjectionDurationMs.Should().BeNull();
        cycle.PauseDurationMs.Should().BeNull();
    }

    // Находка 1 (Critical): в SemiAuto цикл закрывается ДВАЖДЫ по одному физическому
    // впрыску — сначала по смене счётчика (auto/N → auto/N+1), затем ещё раз по уходу
    // из auto (auto/N+1 → idle/N+1), потому что коннектор объявляет idle отдельным
    // сообщением, пока оператор вынимает изделие. Оба ImmCycle пишутся законно
    // (это два ImmCycle-события — один настоящий и один по смене режима), но
    // ActualQuantity обязано вырасти ровно один раз — за цикл, закрытый счётчиком.
    [Fact]
    public async SystemTask SemiAuto_counter_then_idle_counts_output_exactly_once()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 2, PartWeightGrams = 10m, RunnerWeightGrams = 5m };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask
        {
            ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm,
            PlanQuantity = 1000, Status = EntityTaskStatus.InProgress, WorkMode = WorkMode.SemiAuto
        };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var taskOutputHandler = new TaskOutputHandler(db, Substitute.For<IEmulatorControlService>());
        var sut = new CycleProcessingHandler(db, tracker, [taskOutputHandler], NullLogger<CycleProcessingHandler>.Instance);

        // auto/N: первое сообщение — просто фиксирует старт цикла в трекере.
        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
        // auto/N+1: счётчик вырос → цикл A закрыт по counterChanged → +Cavities.
        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto"));
        // idle/N+1: тот же физический впрыск, коннектор объявил idle отдельно →
        // цикл B закрыт по modeChangedFromAuto — НЕ должен засчитаться повторно.
        await sut.ProcessAsync(MakeCycleContext(immId, 6, "idle"));

        (await db.ImmCycles.CountAsync()).Should().Be(2, "оба ImmCycle-события по-прежнему пишутся");

        var reloaded = await db.ShiftTasks.FindAsync(task.Id);
        reloaded!.ActualQuantity.Should().Be(2, "выпуск засчитан ровно один раз — за цикл, закрытый счётчиком");
    }
}
