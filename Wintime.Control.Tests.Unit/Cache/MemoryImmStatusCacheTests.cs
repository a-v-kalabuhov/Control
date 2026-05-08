using FluentAssertions;
using Wintime.Control.Infrastructure.Cache;

namespace Wintime.Control.Tests.Unit.Cache;

public class MemoryImmStatusCacheTests
{
    // =========================================================================
    // GetStatus
    // =========================================================================

    /// <summary>
    /// Если устройство ни разу не добавлялось в кэш, <c>GetStatus</c> должен
    /// вернуть <c>null</c>, а не выбросить исключение.
    /// </summary>
    [Fact]
    public void GetStatus_UnknownDevice_ReturnsNull()
    {
        var cache = new MemoryImmStatusCache();

        cache.GetStatus(Guid.NewGuid()).Should().BeNull();
    }

    /// <summary>
    /// После <c>SetStatus</c> метод <c>GetStatus</c> должен вернуть именно ту
    /// строку статуса, которая была передана при сохранении.
    /// </summary>
    [Fact]
    public void GetStatus_AfterSetStatus_ReturnsCorrectStatus()
    {
        var cache = new MemoryImmStatusCache();
        var id = Guid.NewGuid();

        cache.SetStatus(id, "Auto", DateTime.UtcNow);

        cache.GetStatus(id).Should().Be("Auto");
    }

    /// <summary>
    /// Статусы разных устройств не должны влиять друг на друга:
    /// запрос по одному <c>ImmId</c> должен возвращать только его статус.
    /// </summary>
    [Fact]
    public void GetStatus_MultipleDevices_ReturnsStatusForCorrectDevice()
    {
        var cache = new MemoryImmStatusCache();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        cache.SetStatus(id1, "Auto",   DateTime.UtcNow);
        cache.SetStatus(id2, "Alarm",  DateTime.UtcNow);

        cache.GetStatus(id1).Should().Be("Auto");
        cache.GetStatus(id2).Should().Be("Alarm");
    }

    // =========================================================================
    // SetStatus
    // =========================================================================

    /// <summary>
    /// Повторный вызов <c>SetStatus</c> для того же устройства должен полностью
    /// заменить предыдущую запись новым статусом. В отличие от <c>MemoryImmCache.AddImm</c>,
    /// здесь используется прямое присваивание, а не <c>TryAdd</c>.
    /// </summary>
    [Fact]
    public void SetStatus_CalledTwiceForSameDevice_ReplacesExistingStatus()
    {
        var cache = new MemoryImmStatusCache();
        var id = Guid.NewGuid();
        cache.SetStatus(id, "Idle", DateTime.UtcNow);

        cache.SetStatus(id, "Manual", DateTime.UtcNow);

        cache.GetStatus(id).Should().Be("Manual");
    }

    /// <summary>
    /// Запись в кэше должна содержать корректный <c>ImmId</c>, переданный
    /// в <c>SetStatus</c>, — это поле используется для идентификации устройства
    /// при выгрузке всех статусов через <c>GetAll</c>.
    /// </summary>
    [Fact]
    public void SetStatus_StoresCorrectImmId()
    {
        var cache = new MemoryImmStatusCache();
        var id = Guid.NewGuid();

        cache.SetStatus(id, "Auto", DateTime.UtcNow);

        cache.GetAll().Single().ImmId.Should().Be(id);
    }

    /// <summary>
    /// Поле <c>SinceUtc</c> должно точно сохранять переданный момент времени —
    /// он используется для вычисления длительности текущего статуса в отчётах
    /// и на дашборде.
    /// </summary>
    [Fact]
    public void SetStatus_StoresCorrectSinceUtc()
    {
        var cache = new MemoryImmStatusCache();
        var id = Guid.NewGuid();
        var sinceUtc = new DateTime(2024, 6, 1, 10, 30, 0, DateTimeKind.Utc);

        cache.SetStatus(id, "Auto", sinceUtc);

        cache.GetAll().Single().SinceUtc.Should().Be(sinceUtc);
    }

    // =========================================================================
    // GetAll
    // =========================================================================

    /// <summary>
    /// Когда кэш пуст, <c>GetAll</c> должен возвращать пустой список,
    /// а не <c>null</c>.
    /// </summary>
    [Fact]
    public void GetAll_EmptyCache_ReturnsEmptyList()
    {
        new MemoryImmStatusCache().GetAll().Should().BeEmpty();
    }

    /// <summary>
    /// <c>GetAll</c> должен возвращать записи для всех устройств, которым
    /// был установлен статус. Количество элементов и их <c>ImmId</c> должны
    /// совпадать с вызовами <c>SetStatus</c>.
    /// </summary>
    [Fact]
    public void GetAll_MultipleDevices_ReturnsAllEntries()
    {
        var cache = new MemoryImmStatusCache();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        cache.SetStatus(id1, "Auto",    DateTime.UtcNow);
        cache.SetStatus(id2, "Manual",  DateTime.UtcNow);
        cache.SetStatus(id3, "Offline", DateTime.UtcNow);

        var all = cache.GetAll();

        all.Should().HaveCount(3);
        all.Select(e => e.ImmId).Should().Contain([id1, id2, id3]);
    }

    /// <summary>
    /// После повторного вызова <c>SetStatus</c> метод <c>GetAll</c> должен
    /// отражать обновлённый статус устройства, а не прежний. Количество записей
    /// при этом не должно увеличиваться.
    /// </summary>
    [Fact]
    public void GetAll_AfterStatusUpdate_ReflectsNewStatusWithoutDuplicates()
    {
        var cache = new MemoryImmStatusCache();
        var id = Guid.NewGuid();
        cache.SetStatus(id, "Idle",   DateTime.UtcNow);
        cache.SetStatus(id, "Alarm",  DateTime.UtcNow);

        var all = cache.GetAll();

        all.Should().HaveCount(1);
        all.Single().Status.Should().Be("Alarm");
    }
}
