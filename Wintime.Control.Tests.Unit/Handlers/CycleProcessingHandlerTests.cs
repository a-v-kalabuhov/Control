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

    private static MqttProcessingContext MakeContext(
        Guid immId, string mode,
        CycleSnapshot? currentCycle = null, CompletedCycleSnapshot? lastCycle = null)
    {
        var template = PipelineTestFixtures.MakeTemplate([]);
        var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("0", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: sensors, mode: mode, currentCycle: currentCycle, lastCycle: lastCycle);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
    }

    [Fact]
    public async SystemTask CurrentCycle_opens_immcycle_row_with_null_end_time()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var inj = start.AddMilliseconds(800);
        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, start, inj, Cushion: 5.0m)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.StartTime.Should().Be(start);
        cycle.EndTime.Should().BeNull("цикл ещё не завершён");
        cycle.CycleNumber.Should().Be(1);
        cycle.InjectionStartTime.Should().Be(inj);
    }

    [Fact]
    public async SystemTask LastCycle_closes_matching_open_row_and_runs_handlers()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, tracker, [spy], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var inj = start.AddMilliseconds(800);
        var end = start.AddSeconds(12);

        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, start, inj, Cushion: 5.2m)));
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start, end, inj, Cushion: 5.1m)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.EndTime.Should().Be(end);
        cycle.DurationSeconds.Should().Be(12);
        cycle.InjectionDurationMs.Should().Be((int)(end - inj).TotalMilliseconds);
        cycle.Cushion.Should().Be(5.1m);
        cycle.IsSuccessful.Should().BeTrue();
        spy.Called.Should().BeTrue();
    }

    [Fact]
    public async SystemTask Handler_failure_does_not_prevent_other_handlers_from_running()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var throwing = new ThrowingHandler();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, tracker, [throwing, spy], NullLogger<CycleProcessingHandler>.Instance);

        var start = DateTime.UtcNow;
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start, start.AddSeconds(10), null, null)));

        throwing.Called.Should().BeTrue();
        spy.Called.Should().BeTrue("сбой одного хендлера не прерывает конвейер");
    }

    [Fact]
    public async SystemTask LastCycle_without_open_row_creates_and_closes_in_one_step()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);
        // Control не видела currentCycle этого цикла (пропущенное сообщение/рестарт Control)
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(7, start, end, null, null)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.StartTime.Should().Be(start);
        cycle.EndTime.Should().Be(end);
        cycle.DurationSeconds.Should().Be(10);
    }

    [Fact]
    public async SystemTask Repeated_lastCycle_with_same_number_and_start_time_is_not_reprocessed()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, tracker, [spy], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);
        var last = new CompletedCycleSnapshot(1, start, end, null, null);

        await sut.ProcessAsync(MakeContext(immId, "auto", lastCycle: last));
        await sut.ProcessAsync(MakeContext(immId, "auto", lastCycle: last)); // повтор — блок публикуется в каждом сообщении

        (await db.ImmCycles.CountAsync()).Should().Be(1, "дедупликация по (Number, StartTime)");
    }

    [Fact]
    public async SystemTask Reused_cycle_number_with_different_start_time_creates_two_distinct_rows()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start1 = new DateTime(2026, 8, 9, 8, 0, 0, DateTimeKind.Utc);
        var start2 = new DateTime(2026, 8, 9, 14, 0, 0, DateTimeKind.Utc); // серия сбросилась, тот же Number=1

        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start1, start1.AddSeconds(10), null, null)));
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start2, start2.AddSeconds(10), null, null)));

        (await db.ImmCycles.CountAsync()).Should().Be(2, "разные StartTime — разные физические циклы, несмотря на одинаковый Number");
    }

    [Fact]
    public async SystemTask Tracker_rebuilt_after_control_restart_finds_existing_open_row_via_db_check()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var current = new CycleSnapshot(1, start, null, null);

        var trackerBeforeRestart = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sutBeforeRestart = new CycleProcessingHandler(db, trackerBeforeRestart, [], NullLogger<CycleProcessingHandler>.Instance);
        await sutBeforeRestart.ProcessAsync(MakeContext(immId, "auto", currentCycle: current));

        // "Рестарт Control" — новый трекер без памяти, тот же коннектор повторяет currentCycle
        var trackerAfterRestart = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sutAfterRestart = new CycleProcessingHandler(db, trackerAfterRestart, [], NullLogger<CycleProcessingHandler>.Instance);
        await sutAfterRestart.ProcessAsync(MakeContext(immId, "auto", currentCycle: current));

        (await db.ImmCycles.CountAsync()).Should().Be(1, "ЗА-проверка в БД предотвращает дубль после рестарта Control");
    }

    [Fact]
    public async SystemTask Setup_task_gates_cycle_write_even_with_pending_currentCycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 4 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.Setup };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, DateTime.UtcNow, null, null)));

        (await db.ImmCycles.CountAsync()).Should().Be(0, "Setup (наладка) гасит запись цикла — ShouldProcessCycle=false");
    }

    [Fact]
    public async SystemTask InProgress_cycle_snapshots_cavities_and_task_at_open_time()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 4 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.InProgress };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, DateTime.UtcNow, null, null)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.Cavities.Should().Be(4);
        cycle.TaskId.Should().Be(task.Id);
        cycle.MoldId.Should().Be(mold.Id);
    }

    [Fact]
    public async SystemTask Alarm_mode_at_close_marks_cycle_unsuccessful()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start = DateTime.UtcNow;
        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, start, null, null)));
        await sut.ProcessAsync(MakeContext(immId, "ALARM",
            lastCycle: new CompletedCycleSnapshot(1, start, start.AddSeconds(5), null, null)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.IsSuccessful.Should().BeFalse("режим ALARM в сообщении, закрывающем цикл, → цикл неуспешен");
    }
}
