using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Services;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Services;

public class ImmStatusServiceTests : IDisposable
{
    private readonly ControlDbContext _dbContext;
    private readonly IImmStatusCache _statusCache = Substitute.For<IImmStatusCache>();

    public ImmStatusServiceTests()
    {
        var options = new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ControlDbContext(options);
    }

    public void Dispose() => _dbContext.Dispose();

    private ImmStatusService CreateSut()
        => new(_dbContext, _statusCache, NullLogger<ImmStatusService>.Instance);

    // =========================================================================
    // UpdateStatusAsync — статус не изменился (ранний выход)
    // =========================================================================

    /// <summary>
    /// Если кэш уже содержит тот же статус, что передаётся в <c>UpdateStatusAsync</c>,
    /// метод должен завершиться без записи чего-либо в <c>ImmStatusHistory</c>.
    /// Это ключевая оптимизация: при стабильном режиме ТПА БД не нагружается.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_StatusUnchanged_WritesNothingToDatabase()
    {
        var immId = Guid.NewGuid();
        _statusCache.GetStatus(immId).Returns("Auto");

        await CreateSut().UpdateStatusAsync(immId, "Auto", DateTime.UtcNow);

        _dbContext.ImmStatusHistory.Should().BeEmpty();
    }

    /// <summary>
    /// Если статус не изменился, кэш не должен обновляться — вызов
    /// <c>SetStatus</c> не должен происходить.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_StatusUnchanged_DoesNotUpdateCache()
    {
        var immId = Guid.NewGuid();
        _statusCache.GetStatus(immId).Returns("Idle");

        await CreateSut().UpdateStatusAsync(immId, "Idle", DateTime.UtcNow);

        _statusCache.DidNotReceive().SetStatus(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>());
    }

    // =========================================================================
    // UpdateStatusAsync — первая запись статуса (нет открытой записи в истории)
    // =========================================================================

    /// <summary>
    /// При первом изменении статуса (кэш пуст — устройство только появилось)
    /// должна быть добавлена одна новая запись в <c>ImmStatusHistory</c>
    /// с корректными <c>ImmId</c>, <c>Status</c> и <c>ChangedAt</c>.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_FirstTransition_AddsNewHistoryRecord()
    {
        var immId = Guid.NewGuid();
        var changedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        _statusCache.GetStatus(immId).Returns((string?)null); // устройство ещё не в кэше

        await CreateSut().UpdateStatusAsync(immId, "Auto", changedAt);

        var record = await _dbContext.ImmStatusHistory.SingleAsync();
        record.ImmId.Should().Be(immId);
        record.Status.Should().Be("Auto");
        record.ChangedAt.Should().Be(changedAt);
    }

    /// <summary>
    /// Новая запись истории должна иметь <c>EndedAt = null</c> — это означает,
    /// что данный статус является текущим и период ещё не завершён.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_FirstTransition_NewRecordHasNullEndedAt()
    {
        var immId = Guid.NewGuid();
        _statusCache.GetStatus(immId).Returns((string?)null);

        await CreateSut().UpdateStatusAsync(immId, "Auto", DateTime.UtcNow);

        var record = await _dbContext.ImmStatusHistory.SingleAsync();
        record.EndedAt.Should().BeNull();
    }

    /// <summary>
    /// После успешной записи в БД кэш должен быть обновлён с новым статусом,
    /// правильным <c>ImmId</c> и тем же значением <c>changedAt</c>.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_FirstTransition_UpdatesCache()
    {
        var immId = Guid.NewGuid();
        var changedAt = DateTime.UtcNow;
        _statusCache.GetStatus(immId).Returns((string?)null);

        await CreateSut().UpdateStatusAsync(immId, "Manual", changedAt);

        _statusCache.Received(1).SetStatus(immId, "Manual", changedAt);
    }

    // =========================================================================
    // UpdateStatusAsync — смена статуса при наличии открытой записи в истории
    // =========================================================================

    /// <summary>
    /// Если в <c>ImmStatusHistory</c> уже есть открытая запись (с <c>EndedAt = null</c>),
    /// она должна быть закрыта: поле <c>EndedAt</c> должно принять значение <c>changedAt</c>.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_WithOpenRecord_ClosesExistingRecord()
    {
        var immId = Guid.NewGuid();
        var startedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var changedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedOpenRecord(immId, "Idle", startedAt);
        _statusCache.GetStatus(immId).Returns("Idle");

        await CreateSut().UpdateStatusAsync(immId, "Auto", changedAt);

        var closedRecord = await _dbContext.ImmStatusHistory
            .FirstAsync(h => h.Status == "Idle");
        closedRecord.EndedAt.Should().Be(changedAt);
    }

    /// <summary>
    /// После закрытия предыдущей записи должна быть добавлена новая запись
    /// с новым статусом и <c>EndedAt = null</c>.
    /// В итоге в таблице должно быть ровно две записи: одна закрытая, одна открытая.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_WithOpenRecord_AddsNewOpenRecord()
    {
        var immId = Guid.NewGuid();
        var changedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedOpenRecord(immId, "Idle", changedAt.AddHours(-2));
        _statusCache.GetStatus(immId).Returns("Idle");

        await CreateSut().UpdateStatusAsync(immId, "Auto", changedAt);

        var records = await _dbContext.ImmStatusHistory.ToListAsync();
        records.Should().HaveCount(2);
        records.Should().ContainSingle(r => r.Status == "Auto" && r.EndedAt == null);
        records.Should().ContainSingle(r => r.Status == "Idle" && r.EndedAt != null);
    }

    /// <summary>
    /// Новая запись истории должна иметь корректные <c>ImmId</c>, <c>Status</c>
    /// и <c>ChangedAt</c>, взятые из аргументов вызова.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_WithOpenRecord_NewRecordHasCorrectFields()
    {
        var immId = Guid.NewGuid();
        var changedAt = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedOpenRecord(immId, "Idle", changedAt.AddHours(-2));
        _statusCache.GetStatus(immId).Returns("Idle");

        await CreateSut().UpdateStatusAsync(immId, "Alarm", changedAt);

        var newRecord = await _dbContext.ImmStatusHistory
            .FirstAsync(h => h.Status == "Alarm");
        newRecord.ImmId.Should().Be(immId);
        newRecord.ChangedAt.Should().Be(changedAt);
        newRecord.EndedAt.Should().BeNull();
    }

    /// <summary>
    /// Если запись в истории есть, но у неё уже проставлен <c>EndedAt</c>
    /// (закрытая запись), она не должна изменяться — открытой записи нет,
    /// поэтому обновлять нечего.
    /// </summary>
    [Fact]
    public async Task UpdateStatusAsync_NoOpenRecord_DoesNotModifyClosedRecords()
    {
        var immId = Guid.NewGuid();
        var closedEndedAt = new DateTime(2024, 6, 1, 11, 0, 0, DateTimeKind.Utc);

        _dbContext.ImmStatusHistory.Add(new ImmStatusHistory
        {
            ImmId = immId,
            Status = "Idle",
            ChangedAt = new DateTime(2024, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            EndedAt = closedEndedAt // уже закрытая запись
        });
        await _dbContext.SaveChangesAsync();
        _statusCache.GetStatus(immId).Returns("Manual"); // статус другой

        await CreateSut().UpdateStatusAsync(immId, "Auto", DateTime.UtcNow);

        var closedRecord = await _dbContext.ImmStatusHistory
            .FirstAsync(h => h.Status == "Idle");
        closedRecord.EndedAt.Should().Be(closedEndedAt, "закрытая запись не должна изменяться");
    }

    // =========================================================================
    // GetCurrentStatus
    // =========================================================================

    /// <summary>
    /// <c>GetCurrentStatus</c> должен возвращать значение, полученное из кэша,
    /// не обращаясь к базе данных.
    /// </summary>
    [Fact]
    public void GetCurrentStatus_DelegatesToCache()
    {
        var immId = Guid.NewGuid();
        _statusCache.GetStatus(immId).Returns("Alarm");

        var result = CreateSut().GetCurrentStatus(immId);

        result.Should().Be("Alarm");
    }

    /// <summary>
    /// Если устройство не найдено в кэше, <c>GetCurrentStatus</c> должен
    /// вернуть <c>null</c>, а не выбросить исключение.
    /// </summary>
    [Fact]
    public void GetCurrentStatus_UnknownDevice_ReturnsNull()
    {
        _statusCache.GetStatus(Arg.Any<Guid>()).Returns((string?)null);

        var result = CreateSut().GetCurrentStatus(Guid.NewGuid());

        result.Should().BeNull();
    }

    // =========================================================================
    // GetAllStatuses
    // =========================================================================

    /// <summary>
    /// <c>GetAllStatuses</c> должен возвращать список статусов, полученный
    /// напрямую из кэша, не обращаясь к базе данных.
    /// </summary>
    [Fact]
    public void GetAllStatuses_DelegatesToCache()
    {
        var entries = new List<ImmStatusEntry>
        {
            new(Guid.NewGuid(), "Auto",   DateTime.UtcNow),
            new(Guid.NewGuid(), "Offline", DateTime.UtcNow)
        };
        _statusCache.GetAll().Returns(entries);

        var result = CreateSut().GetAllStatuses();

        result.Should().BeEquivalentTo(entries);
    }

    // =========================================================================
    // Вспомогательные методы
    // =========================================================================

    private async Task SeedOpenRecord(Guid immId, string status, DateTime changedAt)
    {
        _dbContext.ImmStatusHistory.Add(new ImmStatusHistory
        {
            ImmId = immId,
            Status = status,
            ChangedAt = changedAt,
            EndedAt = null
        });
        await _dbContext.SaveChangesAsync();
    }
}
