using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Cache;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Wintime.Control.Infrastructure.Workers;
using Xunit;

namespace Wintime.Control.Tests.Unit.Workers;

public class ImmOfflineWorkerTests
{
    private const int TemplateTimeoutSeconds = 30;

    private sealed record Harness(
        ImmOfflineWorker Worker,
        ControlDbContext Db,
        MemoryImmStatusCache StatusCache,
        MemoryImmCache ImmCache,
        Guid ImmId);

    /// <summary>
    /// ТПА с шаблоном (таймаут 30 с) и статусом, восстановленным в кеш статусов из БД —
    /// как после перезапуска API. Именованный in-memory провайдер общий для сидирующего
    /// контекста и контекста из скоупа воркера (см. DowntimeDetectionWorkerTests).
    /// </summary>
    private static Harness Build(string restoredStatus)
    {
        var dbName = Guid.NewGuid().ToString();
        var statusCache = new MemoryImmStatusCache();
        var immCache = new MemoryImmCache();
        var templateId = Guid.NewGuid();
        var templateCache = Substitute.For<ITemplateCache>();
        templateCache.GetById(templateId).Returns(new CachedTemplate(
            templateId, "T", DateTime.UtcNow, TemplateTimeoutSeconds, new List<Wintime.Control.Core.Entities.SensorTemplate>()));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ControlDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IImmStatusCache>(statusCache);
        services.AddScoped<IImmStatusService, ImmStatusService>();
        var provider = services.BuildServiceProvider();

        var db = provider.GetRequiredService<ControlDbContext>();
        var immId = Guid.NewGuid();
        db.Imms.Add(new Wintime.Control.Core.Entities.Imm
            { Id = immId, Name = "ТПА-4", TemplateId = templateId, IsActive = true });
        db.ImmStatusHistory.Add(new Wintime.Control.Core.Entities.ImmStatusHistory
            { ImmId = immId, Status = restoredStatus, ChangedAt = DateTime.UtcNow.AddHours(-1) });
        db.SaveChanges();
        statusCache.SetStatus(immId, restoredStatus, DateTime.UtcNow.AddHours(-1));

        var worker = new ImmOfflineWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), immCache, statusCache, templateCache,
            NullLogger<ImmOfflineWorker>.Instance);
        return new Harness(worker, db, statusCache, immCache, immId);
    }

    [Fact]
    public async Task RunOnce_RestoredStatus_NoMessagesSinceStart_PastTemplateTimeout_SetsOffline()
    {
        // ПЗП: API перезапущен, пока коннектор молчит. В IImmCache ТПА нет — раньше воркер
        // его не видел, и статус Auto застывал до первого сообщения.
        var h = Build(ImmStatus.Auto);

        await h.Worker.RunOnceAsync(DateTime.UtcNow.AddSeconds(TemplateTimeoutSeconds + 1), CancellationToken.None);

        h.StatusCache.GetStatus(h.ImmId).Should().Be(ImmStatus.Offline);
        h.Db.ImmStatusHistory.Count(x => x.ImmId == h.ImmId && x.Status == ImmStatus.Offline).Should().Be(1);
    }

    [Fact]
    public async Task RunOnce_RestoredStatus_NoMessagesSinceStart_BeforeTemplateTimeout_KeepsStatus()
    {
        var h = Build(ImmStatus.Auto);

        await h.Worker.RunOnceAsync(DateTime.UtcNow.AddSeconds(TemplateTimeoutSeconds - 5), CancellationToken.None);

        h.StatusCache.GetStatus(h.ImmId).Should().Be(ImmStatus.Auto);
    }

    [Fact]
    public async Task RunOnce_RecentMessage_KeepsStatus()
    {
        var h = Build(ImmStatus.Auto);
        var now = DateTime.UtcNow.AddMinutes(10);
        h.ImmCache.UpdateEntry(h.ImmId, now.AddSeconds(-5), TemplateTimeoutSeconds, new Dictionary<string, string>());

        await h.Worker.RunOnceAsync(now, CancellationToken.None);

        h.StatusCache.GetStatus(h.ImmId).Should().Be(ImmStatus.Auto);
    }

    [Fact]
    public async Task RunOnce_LastMessageOlderThanTimeout_SetsOffline()
    {
        var h = Build(ImmStatus.Idle);
        var now = DateTime.UtcNow;
        h.ImmCache.UpdateEntry(h.ImmId, now.AddSeconds(-(TemplateTimeoutSeconds + 1)), TemplateTimeoutSeconds,
            new Dictionary<string, string>());

        await h.Worker.RunOnceAsync(now, CancellationToken.None);

        h.StatusCache.GetStatus(h.ImmId).Should().Be(ImmStatus.Offline);
    }

    [Fact]
    public async Task RunOnce_AlreadyOffline_WritesNothing()
    {
        var h = Build(ImmStatus.Offline);

        await h.Worker.RunOnceAsync(DateTime.UtcNow.AddHours(1), CancellationToken.None);

        h.Db.ImmStatusHistory.Count(x => x.ImmId == h.ImmId).Should().Be(1);
    }
}
