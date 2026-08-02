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

    private static MqttProcessingContext MakeCycleContext(Guid immId, int counter, string mode, DateTime? timestampUtc = null)
    {
        var sensor = PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter");
        var template = PipelineTestFixtures.MakeTemplate([sensor]);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string> { ["counter"] = counter.ToString() }, mode: mode, timestampUtc: timestampUtc);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
    }

    /// <summary>
    /// Контекст с тремя сенсорами: счётчик + защёлкнутые границы цикла литья
    /// (cycleStart/cycleEnd, unix-мс).
    /// </summary>
    private static MqttProcessingContext MakeCycleContextWithBoundaries(
        Guid immId, int counter, string mode, long cycleStartMs, long cycleEndMs, DateTime? timestampUtc = null)
    {
        var sensors = new[]
        {
            PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter"),
            PipelineTestFixtures.MakeSensor("cs", type: "cycleStart"),
            PipelineTestFixtures.MakeSensor("ce", type: "cycleEnd")
        };
        var template = PipelineTestFixtures.MakeTemplate(sensors);
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: new Dictionary<string, string>
            {
                ["counter"] = counter.ToString(),
                ["cs"] = cycleStartMs.ToString(),
                ["ce"] = cycleEndMs.ToString()
            }, mode: mode, timestampUtc: timestampUtc);
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
    public async SystemTask Two_consecutive_cycles_derive_boundaries_from_latched_sensors()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        const long t0 = 1_700_000_000_000L; // cycleStart(1)
        const long t1 = t0 + 12_300;        // cycleEnd(1)   — InjectionDurationMs(1) = 12300
        const long t2 = t1 + 3_200;         // cycleStart(2) — PauseDurationMs(2) = 3200
        const long t3 = t2 + 12_200;        // cycleEnd(2)   — InjectionDurationMs(2) = 12200

        // Первое сообщение — только сеет трекер (состояние ещё пустое).
        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
        // Закрывает цикл 1: cycleEnd(0) неизвестен → PauseDurationMs = null,
        // StartTime падает на cycleStart(1) за неимением предыдущей границы.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1));
        // Закрывает цикл 2: cycleEnd(1) = t1 уже известен трекеру → StartTime(2) = t1,
        // PauseDurationMs(2) = cycleStart(2) - cycleEnd(1).
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t2, t3));

        var cycles = await db.ImmCycles.OrderBy(c => c.StartTime).ToListAsync();
        cycles.Should().HaveCount(2);
        var cycle1 = cycles[0];
        var cycle2 = cycles[1];

        cycle1.InjectionDurationMs.Should().Be(12300);
        cycle1.PauseDurationMs.Should().BeNull("cycleEnd(0) неизвестен — первый цикл после рестарта");
        cycle1.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t0).UtcDateTime);
        cycle1.EndTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t1).UtcDateTime);
        cycle1.DurationSeconds.Should().Be(12);

        cycle2.InjectionDurationMs.Should().Be(12200);
        cycle2.PauseDurationMs.Should().Be(3200);
        cycle2.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t1).UtcDateTime, "StartTime = cycleEnd предыдущего цикла");
        cycle2.EndTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t3).UtcDateTime);
        cycle2.DurationSeconds.Should().Be(15);
        cycle2.DurationSeconds.Should().Be(
            (int)Math.Round((cycle2.InjectionDurationMs!.Value + cycle2.PauseDurationMs!.Value) / 1000.0),
            "инвариант: DurationSeconds и сумма длительностей описывают один и тот же отрезок");
    }

    [Fact]
    public async SystemTask Stale_cycle_end_latch_falls_back_to_message_timestamps()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        const long t0 = 1_700_000_000_000L;
        const long t1 = t0 + 12_300;
        var msg1Time = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
        var msg2Time = new DateTime(2024, 1, 1, 12, 0, 2, DateTimeKind.Utc);

        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
        // Закрывает цикл 1 нормально, LastCycleEndMs трекера становится t1.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1, msg1Time));
        // cycleEnd(2) совпадает с cycleEnd(1) — защёлка не обновилась.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t1 + 5_000, t1, msg2Time));

        var cycle2 = await db.ImmCycles.OrderBy(c => c.StartTime).Skip(1).SingleAsync();
        cycle2.InjectionDurationMs.Should().BeNull("протухшая защёлка → запасной путь");
        cycle2.PauseDurationMs.Should().BeNull();
        cycle2.StartTime.Should().Be(msg1Time, "запасной путь: старт — метка предыдущего сообщения из трекера");
        cycle2.EndTime.Should().Be(msg2Time, "запасной путь: конец — метка текущего сообщения");
    }

    [Fact]
    public async SystemTask Inverted_cycle_boundaries_fall_back_to_message_timestamps()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        const long t0 = 1_700_000_000_000L;
        const long t1 = t0 + 12_300;
        var msg1Time = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
        var msg2Time = new DateTime(2024, 1, 1, 12, 0, 2, DateTimeKind.Utc);

        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1, msg1Time));
        // cycleEnd(2) < cycleStart(2) — границы переставлены; cycleEnd(2) при этом больше
        // cycleEnd(1), так что срабатывает именно проверка на инверсию, а не на протухание.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t1 + 20_000, t1 + 18_000, msg2Time));

        var cycle2 = await db.ImmCycles.OrderBy(c => c.StartTime).Skip(1).SingleAsync();
        cycle2.InjectionDurationMs.Should().BeNull("переставленные границы → запасной путь");
        cycle2.PauseDurationMs.Should().BeNull();
        cycle2.StartTime.Should().Be(msg1Time);
        cycle2.EndTime.Should().Be(msg2Time);
    }

    [Fact]
    public async SystemTask Zero_cycle_start_latch_falls_back_instead_of_overflowing_int32()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        const long t0 = 1_700_000_000_000L;
        const long t1 = t0 + 12_300;
        var msg1Time = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
        var msg2Time = new DateTime(2024, 1, 1, 12, 0, 2, DateTimeKind.Utc);

        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
        // Закрывает цикл 1 нормально, LastCycleEndMs трекера становится t1.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1, msg1Time));
        // cycleStart(2) = 0 (сброшенная/неинициализированная защёлка коннектора), cycleEnd(2) — живое
        // значение. Обе существующие проверки (notStale, notReversed) проходят, но сырая разница
        // (~1.7e12 мс) переполнила бы int32 при приведении — граница должна быть отвергнута.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", 0L, t1 + 20_000, msg2Time));

        var cycle2 = await db.ImmCycles.OrderBy(c => c.StartTime).Skip(1).SingleAsync();
        cycle2.InjectionDurationMs.Should().BeNull("cycleStart=0 → разница переполнила бы int32 → запасной путь");
        cycle2.PauseDurationMs.Should().BeNull();
        cycle2.StartTime.Should().Be(msg1Time, "запасной путь: старт — метка предыдущего сообщения из трекера");
        cycle2.EndTime.Should().Be(msg2Time, "запасной путь: конец — метка текущего сообщения");
    }

    [Fact]
    public async SystemTask CycleStart_older_than_previous_cycleEnd_falls_back_to_message_timestamps()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        const long t0 = 1_700_000_000_000L;
        const long t1 = t0 + 12_300;
        var msg1Time = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
        var msg2Time = new DateTime(2024, 1, 1, 12, 0, 2, DateTimeKind.Utc);

        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
        // Закрывает цикл 1 нормально, LastCycleEndMs трекера становится t1.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1, msg1Time));
        // cycleStart(2) < cycleEnd(1) — защёлка старта отстаёт от предыдущего конца (пропущенное
        // закрытие / частично обновлённая пара защёлок). cycleEnd(2) при этом больше cycleEnd(1)
        // и >= cycleStart(2), так что обе СУЩЕСТВУЮЩИЕ проверки (notStale, notReversed) проходят —
        // именно новая проверка notOlderThanPrevEnd должна отвергнуть границу.
        await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t1 - 1_000, t1 + 5_000, msg2Time));

        var cycle2 = await db.ImmCycles.OrderBy(c => c.StartTime).Skip(1).SingleAsync();
        cycle2.InjectionDurationMs.Should().BeNull("cycleStart(2) < cycleEnd(1) → отрицательная пауза → запасной путь");
        cycle2.PauseDurationMs.Should().BeNull();
        cycle2.StartTime.Should().Be(msg1Time);
        cycle2.EndTime.Should().Be(msg2Time);
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

    // Находка (Important): (int) от дробной разности усекает к нулю — цикл 9.9с
    // записывался бы как 9. С сообщениями, несущими доли секунды, разность больше
    // не целая, поэтому DurationSeconds обязан округляться к ближайшей секунде.
    [Fact]
    public async SystemTask Fractional_cycle_duration_rounds_to_nearest_second()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var cycleStart = new DateTime(2024, 1, 1, 12, 0, 0, 0, DateTimeKind.Utc);
        var cycleEnd = cycleStart.AddSeconds(9.9); // усечение → 9, округление → 10

        // auto/5: открывает окно цикла в момент cycleStart.
        await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto", cycleStart));
        // auto/6: счётчик изменился 9.9с спустя → цикл закрыт.
        await sut.ProcessAsync(MakeCycleContext(immId, 6, "auto", cycleEnd));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.DurationSeconds.Should().Be(10, "9.9с округляется к ближайшей секунде, а не усекается до 9");
    }
}
