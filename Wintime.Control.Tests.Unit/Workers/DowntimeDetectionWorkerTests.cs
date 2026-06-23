using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Workers;
using Wintime.Control.Shared.Settings;
using Xunit;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Unit.Workers;

public class DowntimeDetectionWorkerTests
{
    /// <summary>
    /// Регистрирует ControlDbContext поверх ФИКСИРОВАННОГО именованного in-memory провайдера.
    /// EF InMemory делит данные по имени БД, поэтому контекст, который сидирует тест,
    /// и контекст, который резолвит воркер из своего скоупа, видят одно и то же хранилище —
    /// без самоссылающейся scoped-фабрики (которая привела бы к StackOverflow).
    /// </summary>
    private static (ControlDbContext SeedDb, IServiceScopeFactory ScopeFactory) BuildDb()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ControlDbContext>(o => o.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();

        var seedDb = provider.GetRequiredService<ControlDbContext>();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return (seedDb, scopeFactory);
    }

    private static DowntimeDetectionWorker BuildWorker(
        IServiceScopeFactory scopeFactory, IImmStatusCache statusCache, int threshold = 120)
    {
        var opts = Options.Create(new DowntimeSettings
        {
            IdleThresholdSeconds = threshold,
            PollingIntervalSeconds = 10
        });
        return new DowntimeDetectionWorker(
            scopeFactory, statusCache, opts, NullLogger<DowntimeDetectionWorker>.Instance);
    }

    [Fact]
    public async Task RunOnce_InProgress_IdlePastThreshold_CreatesAutoDowntime()
    {
        var immId = Guid.NewGuid();
        var (db, scopeFactory) = BuildDb();
        var imm = new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true };
        db.Imms.Add(imm);
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Idle, DateTime.UtcNow.AddSeconds(-200))
        });

        var worker = BuildWorker(scopeFactory, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        var evt = db.Events.Single();
        evt.EventType.Should().Be(Wintime.Control.Core.Enums.EventType.Downtime);
        evt.IsAuto.Should().BeTrue();
        evt.EndTime.Should().BeNull();
        evt.TaskId.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_BackToAuto_ClosesOpenAutoDowntime()
    {
        var immId = Guid.NewGuid();
        var (db, scopeFactory) = BuildDb();
        db.Imms.Add(new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true });
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.Events.Add(new Wintime.Control.Core.Entities.Event
        {
            ImmId = immId, EventType = Wintime.Control.Core.Enums.EventType.Downtime,
            StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = null, IsAuto = true
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Auto, DateTime.UtcNow.AddSeconds(-5))
        });

        var worker = BuildWorker(scopeFactory, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        // Очищаем change tracker сидирующего контекста: воркер изменил Event через
        // отдельный контекст своего скоупа, и без Clear() здесь вернётся устаревший
        // трекнутый экземпляр вместо актуального состояния в хранилище EF InMemory.
        db.ChangeTracker.Clear();
        db.Events.Single().EndTime.Should().NotBeNull();
    }

    [Fact]
    public async Task RunOnce_ManualOpenDowntime_NotClosedByWorker()
    {
        var immId = Guid.NewGuid();
        var (db, scopeFactory) = BuildDb();
        db.Imms.Add(new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true });
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.Events.Add(new Wintime.Control.Core.Entities.Event
        {
            ImmId = immId, EventType = Wintime.Control.Core.Enums.EventType.Downtime,
            StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = null, IsAuto = false // ручной
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Auto, DateTime.UtcNow.AddSeconds(-5))
        });

        var worker = BuildWorker(scopeFactory, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        db.Events.Single().EndTime.Should().BeNull(); // воркер не трогает ручной простой
    }

    [Fact]
    public async Task RunOnce_OpenManualDowntimeExists_DoesNotCreateDuplicateAutoDowntime()
    {
        var immId = Guid.NewGuid();
        var (db, scopeFactory) = BuildDb();
        db.Imms.Add(new Wintime.Control.Core.Entities.Imm { Id = immId, Name = "T", IsActive = true });
        db.ShiftTasks.Add(new EntityTask
        {
            ImmId = immId, Status = EntityTaskStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-1), PlanQuantity = 100
        });
        db.Events.Add(new Wintime.Control.Core.Entities.Event
        {
            ImmId = immId, EventType = Wintime.Control.Core.Enums.EventType.Downtime,
            StartTime = DateTime.UtcNow.AddMinutes(-5), EndTime = null, IsAuto = false // ручной, открыт
        });
        db.SaveChanges();

        var statusCache = Substitute.For<IImmStatusCache>();
        statusCache.GetAll().Returns(new List<ImmStatusEntry>
        {
            new(immId, ImmStatus.Idle, DateTime.UtcNow.AddSeconds(-200)) // не-Auto дольше порога
        });

        var worker = BuildWorker(scopeFactory, statusCache);
        await worker.RunOnceAsync(CancellationToken.None);

        db.ChangeTracker.Clear();
        // Должен остаться только один (ручной) простой — никакого второго авто-простоя.
        db.Events.Should().HaveCount(1);
        db.Events.Single().IsAuto.Should().BeFalse();
    }
}
