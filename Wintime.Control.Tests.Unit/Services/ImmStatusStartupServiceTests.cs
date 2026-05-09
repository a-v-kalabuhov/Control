using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Services;

public sealed class ImmStatusStartupServiceTests : IDisposable
{
    private readonly ControlDbContext _db;
    private readonly IImmStatusCache _statusCache = Substitute.For<IImmStatusCache>();
    private readonly ITemplateCache _templateCache = Substitute.For<ITemplateCache>();
    private readonly ImmStatusStartupService _sut;

    public ImmStatusStartupServiceTests()
    {
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ControlDbContext(options);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(ControlDbContext)).Returns(_db);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        _sut = new ImmStatusStartupService(
            scopeFactory,
            _statusCache,
            _templateCache,
            NullLogger<ImmStatusStartupService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private async Task<(Guid immId, Guid templateId)> SeedImmAsync(int timeoutSeconds = 30)
    {
        var templateId = Guid.NewGuid();
        var immId = Guid.NewGuid();

        _db.Templates.Add(new Template
        {
            Id = templateId,
            CreatedAt = DateTime.UtcNow,
            Name = "Test",
            Manufacturer = "Test",
            Model = "X",
            Author = "Test",
            JsonConfig = "{}",
            IsActive = true,
            UpdatedAt = DateTime.UtcNow
        });
        _db.Imms.Add(new Imm
        {
            Id = immId,
            CreatedAt = DateTime.UtcNow,
            Name = "IMM-TEST",
            TemplateId = templateId
        });
        await _db.SaveChangesAsync();

        _templateCache.GetById(templateId)
            .Returns(new CachedTemplate(templateId, "Test", DateTime.UtcNow, timeoutSeconds, []));

        return (immId, templateId);
    }

    private async Task<ImmStatusHistory> SeedOpenRecordAsync(Guid immId, string status, DateTime changedAt)
    {
        var record = new ImmStatusHistory
        {
            ImmId = immId,
            Status = status,
            ChangedAt = changedAt,
            EndedAt = null
        };
        _db.ImmStatusHistory.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    // =========================================================================
    // Первый запуск — AppHeartbeat отсутствует
    // =========================================================================

    /// <summary>
    /// При первом запуске (нет записи AppHeartbeat) открытые записи со статусом,
    /// отличным от «Offline», должны закрываться немедленно с временем ≈ UtcNow.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoHeartbeat_ClosesOpenNonOfflineRecord()
    {
        var (immId, _) = await SeedImmAsync();
        var record = await SeedOpenRecordAsync(immId, "Auto", DateTime.UtcNow.AddHours(-1));

        var before = DateTime.UtcNow;
        await _sut.StartAsync(CancellationToken.None);
        var after = DateTime.UtcNow;

        record.EndedAt.Should().NotBeNull();
        record.EndedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    /// <summary>
    /// После закрытия записи при первом запуске должна вставляться открытая
    /// запись «Offline» для соответствующего ТПА.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoHeartbeat_InsertsOfflineRecordForClosedImm()
    {
        var (immId, _) = await SeedImmAsync();
        await SeedOpenRecordAsync(immId, "Manual", DateTime.UtcNow.AddHours(-1));

        await _sut.StartAsync(CancellationToken.None);

        var offlineRecord = await _db.ImmStatusHistory
            .FirstOrDefaultAsync(h => h.ImmId == immId && h.Status == "Offline" && h.EndedAt == null);
        offlineRecord.Should().NotBeNull();
    }

    /// <summary>
    /// Открытая запись «Offline» не должна затрагиваться при первом запуске —
    /// фильтр <c>Status != "Offline"</c> исключает её из обработки.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoHeartbeat_DoesNotTouchOpenOfflineRecord()
    {
        var (immId, _) = await SeedImmAsync();
        var offlineRecord = await SeedOpenRecordAsync(immId, "Offline", DateTime.UtcNow.AddHours(-1));

        await _sut.StartAsync(CancellationToken.None);

        offlineRecord.EndedAt.Should().BeNull();
    }

    /// <summary>
    /// При первом запуске должна создаваться запись AppHeartbeat (Id = 1)
    /// с временем, соответствующим моменту старта.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoHeartbeat_CreatesHeartbeatRecord()
    {
        var before = DateTime.UtcNow;
        await _sut.StartAsync(CancellationToken.None);
        var after = DateTime.UtcNow;

        var heartbeat = await _db.AppHeartbeat.FindAsync(1);
        heartbeat.Should().NotBeNull();
        heartbeat!.LastHeartbeatAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // =========================================================================
    // Порог истёк — запись закрывается
    // =========================================================================

    /// <summary>
    /// Если порог (lastHeartbeat + 2 × DeviceTimeoutSeconds) уже прошёл,
    /// открытая запись должна закрываться именно в момент порога, а не в UtcNow.
    /// </summary>
    [Fact]
    public async Task StartAsync_ThresholdPassed_ClosesRecordAtThresholdTime()
    {
        const int timeoutSeconds = 30;
        var (immId, _) = await SeedImmAsync(timeoutSeconds);

        var lastHeartbeat = DateTime.UtcNow.AddHours(-2);
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = lastHeartbeat });
        var record = await SeedOpenRecordAsync(immId, "Auto", lastHeartbeat.AddMinutes(-30));

        await _sut.StartAsync(CancellationToken.None);

        var expectedThreshold = lastHeartbeat.AddSeconds(2 * timeoutSeconds);
        record.EndedAt.Should().Be(expectedThreshold);
    }

    /// <summary>
    /// Вставленная запись «Offline» должна иметь <c>ChangedAt</c>, равный моменту
    /// порога, и оставаться открытой (EndedAt = null).
    /// </summary>
    [Fact]
    public async Task StartAsync_ThresholdPassed_OfflineRecordHasThresholdTimestamp()
    {
        const int timeoutSeconds = 30;
        var (immId, _) = await SeedImmAsync(timeoutSeconds);

        var lastHeartbeat = DateTime.UtcNow.AddHours(-2);
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = lastHeartbeat });
        await SeedOpenRecordAsync(immId, "Auto", lastHeartbeat.AddMinutes(-30));

        await _sut.StartAsync(CancellationToken.None);

        var expectedThreshold = lastHeartbeat.AddSeconds(2 * timeoutSeconds);
        var offlineRecord = await _db.ImmStatusHistory
            .FirstOrDefaultAsync(h => h.ImmId == immId && h.Status == "Offline" && h.EndedAt == null);
        offlineRecord.Should().NotBeNull();
        offlineRecord!.ChangedAt.Should().Be(expectedThreshold);
    }

    // =========================================================================
    // Порог не истёк — запись остаётся открытой
    // =========================================================================

    /// <summary>
    /// Если порог ещё не прошёл, открытая запись не должна закрываться
    /// и новая запись «Offline» не должна вставляться.
    /// </summary>
    [Fact]
    public async Task StartAsync_ThresholdNotPassed_LeavesRecordOpen()
    {
        const int timeoutSeconds = 300; // threshold = 10s ago + 600s → ~590s в будущем
        var (immId, _) = await SeedImmAsync(timeoutSeconds);

        var lastHeartbeat = DateTime.UtcNow.AddSeconds(-10);
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = lastHeartbeat });
        var record = await SeedOpenRecordAsync(immId, "Auto", lastHeartbeat.AddMinutes(-5));

        await _sut.StartAsync(CancellationToken.None);

        record.EndedAt.Should().BeNull();
        var offlineCount = await _db.ImmStatusHistory
            .CountAsync(h => h.ImmId == immId && h.Status == "Offline");
        offlineCount.Should().Be(0);
    }

    // =========================================================================
    // Заполнение кэша статусов (шаг 3)
    // =========================================================================

    /// <summary>
    /// Все текущие открытые записи (EndedAt = null) после сверки должны
    /// загружаться в кэш через <c>SetStatus</c> с корректными аргументами.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithOpenStatus_LoadsItIntoCache()
    {
        const int timeoutSeconds = 300; // threshold далеко в будущем — запись не закроется
        var (immId, _) = await SeedImmAsync(timeoutSeconds);

        var lastHeartbeat = DateTime.UtcNow.AddSeconds(-10);
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = lastHeartbeat });
        var changedAt = lastHeartbeat.AddMinutes(-10);
        await SeedOpenRecordAsync(immId, "Idle", changedAt);

        await _sut.StartAsync(CancellationToken.None);

        _statusCache.Received().SetStatus(immId, "Idle", changedAt);
    }

    /// <summary>
    /// Если открытых записей нет, <c>SetStatus</c> не должен вызываться.
    /// </summary>
    [Fact]
    public async Task StartAsync_NoOpenStatuses_DoesNotCallSetStatus()
    {
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = DateTime.UtcNow.AddSeconds(-5) });
        await _db.SaveChangesAsync();

        await _sut.StartAsync(CancellationToken.None);

        _statusCache.DidNotReceive().SetStatus(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>());
    }

    // =========================================================================
    // Управление AppHeartbeat (шаг 4)
    // =========================================================================

    /// <summary>
    /// При повторном запуске существующая запись AppHeartbeat должна обновляться:
    /// метка времени обновится, дубликатов не появится.
    /// </summary>
    [Fact]
    public async Task StartAsync_ExistingHeartbeat_UpdatesTimestampWithoutDuplicates()
    {
        var oldTime = DateTime.UtcNow.AddHours(-1);
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = oldTime });
        await _db.SaveChangesAsync();

        await _sut.StartAsync(CancellationToken.None);

        var heartbeat = await _db.AppHeartbeat.FindAsync(1);
        heartbeat!.LastHeartbeatAt.Should().BeAfter(oldTime);
        (await _db.AppHeartbeat.CountAsync()).Should().Be(1);
    }

    // =========================================================================
    // Fallback-таймаут (ТПА не найден в БД)
    // =========================================================================

    /// <summary>
    /// Если ТПА отсутствует в таблице Imms, <c>GetDeviceTimeoutSeconds</c> возвращает
    /// 60 с по умолчанию. При свежем heartbeat (< 120 с назад) порог ещё не прошёл
    /// и открытая запись должна оставаться нетронутой.
    /// </summary>
    [Fact]
    public async Task StartAsync_ImmNotFoundInDb_UsesDefaultTimeoutAndLeavesRecordOpen()
    {
        var unknownImmId = Guid.NewGuid(); // нет в таблице Imms
        // threshold = 10s ago + 2 * 60s = 110s в будущем → не закрывается
        var lastHeartbeat = DateTime.UtcNow.AddSeconds(-10);
        _db.AppHeartbeat.Add(new AppHeartbeat { Id = 1, LastHeartbeatAt = lastHeartbeat });
        var record = await SeedOpenRecordAsync(unknownImmId, "Auto", lastHeartbeat.AddMinutes(-5));

        await _sut.StartAsync(CancellationToken.None);

        record.EndedAt.Should().BeNull();
    }
}
