using FluentAssertions;
using Wintime.Control.Infrastructure.Cache;

namespace Wintime.Control.Tests.Unit.Cache;

public class MemoryImmCacheTests
{
    // =========================================================================
    // GetEntry
    // =========================================================================

    /// <summary>
    /// Если устройство ни разу не регистрировалось, <c>GetEntry</c> должен
    /// вернуть <c>null</c>, а не выбросить исключение.
    /// </summary>
    [Fact]
    public void GetEntry_UnknownDevice_ReturnsNull()
    {
        var cache = new MemoryImmCache();

        var result = cache.GetEntry(Guid.NewGuid());

        result.Should().BeNull();
    }

    /// <summary>
    /// После вызова <c>AddImm</c> метод <c>GetEntry</c> должен вернуть
    /// ненулевую запись с тем же <c>ImmId</c>, что был передан при регистрации.
    /// </summary>
    [Fact]
    public void GetEntry_AfterAddImm_ReturnsEntry()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();

        cache.AddImm(id, 60);

        cache.GetEntry(id).Should().NotBeNull();
    }

    // =========================================================================
    // AddImm
    // =========================================================================

    /// <summary>
    /// Новая запись должна содержать корректные <c>ImmId</c> и <c>TimeoutSeconds</c>,
    /// переданные в <c>AddImm</c>.
    /// </summary>
    [Fact]
    public void AddImm_CreatesEntryWithCorrectIdAndTimeout()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();

        cache.AddImm(id, 120);

        var entry = cache.GetEntry(id)!;
        entry.ImmId.Should().Be(id);
        entry.TimeoutSeconds.Should().Be(120);
    }

    /// <summary>
    /// Сразу после <c>AddImm</c> запись должна иметь <c>LastMessageAt = DateTime.MinValue</c> —
    /// это сигнал того, что устройство ещё не прислало ни одного сообщения.
    /// </summary>
    [Fact]
    public void AddImm_SetsLastMessageAtToMinValue()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();

        cache.AddImm(id, 60);

        cache.GetEntry(id)!.LastMessageAt.Should().Be(DateTime.MinValue);
    }

    /// <summary>
    /// Новая запись должна иметь пустой словарь <c>SensorValues</c> —
    /// данных телеметрии ещё нет.
    /// </summary>
    [Fact]
    public void AddImm_CreatesSensorValuesEmpty()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();

        cache.AddImm(id, 60);

        cache.GetEntry(id)!.SensorValues.Should().BeEmpty();
    }

    /// <summary>
    /// Сразу после <c>AddImm</c> свойство <c>IsOnline</c> должно быть <c>false</c>,
    /// потому что <c>LastMessageAt = DateTime.MinValue</c> не попадает под условие онлайн.
    /// </summary>
    [Fact]
    public void AddImm_IsOnlineIsFalse()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();

        cache.AddImm(id, 60);

        cache.GetEntry(id)!.IsOnline.Should().BeFalse();
    }

    /// <summary>
    /// Повторный вызов <c>AddImm</c> для уже зарегистрированного устройства
    /// не должен перезаписывать существующую запись (используется <c>TryAdd</c>).
    /// Это гарантирует, что данные телеметрии, накопленные после первой регистрации,
    /// не будут потеряны.
    /// </summary>
    [Fact]
    public void AddImm_CalledTwiceForSameDevice_DoesNotOverwriteExistingEntry()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        var sensors = new Dictionary<string, string> { ["temp"] = "20.0" };

        cache.AddImm(id, 60);
        cache.UpdateEntry(id, DateTime.UtcNow, 60, sensors);
        cache.AddImm(id, 999); // повторная регистрация не должна сбросить данные

        var entry = cache.GetEntry(id)!;
        entry.TimeoutSeconds.Should().Be(60,     "первый timeout не должен быть заменён");
        entry.SensorValues.Should().ContainKey("temp", "накопленные данные не должны быть потеряны");
    }

    // =========================================================================
    // RemoveImm
    // =========================================================================

    /// <summary>
    /// После <c>RemoveImm</c> метод <c>GetEntry</c> должен вернуть <c>null</c> —
    /// устройство больше не отслеживается кэшем.
    /// </summary>
    [Fact]
    public void RemoveImm_KnownDevice_EntryBecomesNull()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        cache.AddImm(id, 60);

        cache.RemoveImm(id);

        cache.GetEntry(id).Should().BeNull();
    }

    /// <summary>
    /// Вызов <c>RemoveImm</c> для незарегистрированного устройства не должен
    /// приводить к исключению — операция должна завершиться молча.
    /// </summary>
    [Fact]
    public void RemoveImm_UnknownDevice_DoesNotThrow()
    {
        var cache = new MemoryImmCache();

        var act = () => cache.RemoveImm(Guid.NewGuid());

        act.Should().NotThrow();
    }

    // =========================================================================
    // UpdateEntry
    // =========================================================================

    /// <summary>
    /// Если устройство не было зарегистрировано через <c>AddImm</c>,
    /// <c>UpdateEntry</c> всё равно должен создать запись (поведение <c>AddOrUpdate</c>).
    /// </summary>
    [Fact]
    public void UpdateEntry_UnknownDevice_CreatesNewEntry()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        var sensors = new Dictionary<string, string> { ["s"] = "1" };

        cache.UpdateEntry(id, DateTime.UtcNow, 60, sensors);

        cache.GetEntry(id).Should().NotBeNull();
    }

    /// <summary>
    /// После <c>UpdateEntry</c> поле <c>LastMessageAt</c> должно принять
    /// значение, переданное в вызове, — именно по нему отслеживается онлайн-статус.
    /// </summary>
    [Fact]
    public void UpdateEntry_ExistingDevice_UpdatesLastMessageAt()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        cache.AddImm(id, 60);
        var newTime = DateTime.UtcNow;

        cache.UpdateEntry(id, newTime, 60, new Dictionary<string, string>());

        cache.GetEntry(id)!.LastMessageAt.Should().Be(newTime);
    }

    /// <summary>
    /// После <c>UpdateEntry</c> словарь <c>SensorValues</c> должен содержать
    /// значения, переданные в вызове, а не те, что были до него.
    /// </summary>
    [Fact]
    public void UpdateEntry_ExistingDevice_ReplacesSensorValues()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        cache.AddImm(id, 60);
        cache.UpdateEntry(id, DateTime.UtcNow, 60, new Dictionary<string, string> { ["old"] = "0" });

        var newSensors = new Dictionary<string, string> { ["temp"] = "25.0" };
        cache.UpdateEntry(id, DateTime.UtcNow, 60, newSensors);

        var entry = cache.GetEntry(id)!;
        entry.SensorValues.Should().ContainKey("temp").And.NotContainKey("old");
    }

    /// <summary>
    /// <c>UpdateEntry</c> не должен изменять <c>ImmId</c> существующей записи —
    /// запись идентифицируется по нему, и его замена нарушила бы целостность кэша.
    /// </summary>
    [Fact]
    public void UpdateEntry_ExistingDevice_DoesNotChangeImmId()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        cache.AddImm(id, 60);

        cache.UpdateEntry(id, DateTime.UtcNow, 60, new Dictionary<string, string>());

        cache.GetEntry(id)!.ImmId.Should().Be(id);
    }

    /// <summary>
    /// После <c>UpdateEntry</c> с временем в пределах таймаута устройство
    /// должно быть помечено как онлайн (<c>IsOnline = true</c>).
    /// </summary>
    [Fact]
    public void UpdateEntry_RecentTimestamp_IsOnlineBecomesTrue()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        cache.AddImm(id, 60);

        cache.UpdateEntry(id, DateTime.UtcNow, 60, new Dictionary<string, string>());

        cache.GetEntry(id)!.IsOnline.Should().BeTrue();
    }

    /// <summary>
    /// Если <c>LastMessageAt</c> старше <c>TimeoutSeconds</c>, устройство
    /// должно быть помечено как офлайн (<c>IsOnline = false</c>).
    /// </summary>
    [Fact]
    public void UpdateEntry_ExpiredTimestamp_IsOnlineIsFalse()
    {
        var cache = new MemoryImmCache();
        var id = Guid.NewGuid();
        cache.AddImm(id, 60);

        var expiredTime = DateTime.UtcNow.AddSeconds(-120); // вдвое старше таймаута
        cache.UpdateEntry(id, expiredTime, 60, new Dictionary<string, string>());

        cache.GetEntry(id)!.IsOnline.Should().BeFalse();
    }

    // =========================================================================
    // GetAll
    // =========================================================================

    /// <summary>
    /// Когда в кэше нет ни одного устройства, <c>GetAll</c> должен вернуть
    /// пустой список, а не <c>null</c>.
    /// </summary>
    [Fact]
    public void GetAll_EmptyCache_ReturnsEmptyList()
    {
        var cache = new MemoryImmCache();

        cache.GetAll().Should().BeEmpty();
    }

    /// <summary>
    /// <c>GetAll</c> должен вернуть ровно столько элементов, сколько устройств
    /// было зарегистрировано, включая все их идентификаторы.
    /// </summary>
    [Fact]
    public void GetAll_MultipleDevicesRegistered_ReturnsAllEntries()
    {
        var cache = new MemoryImmCache();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        cache.AddImm(id1, 60);
        cache.AddImm(id2, 60);
        cache.AddImm(id3, 60);

        var all = cache.GetAll();

        all.Should().HaveCount(3);
        all.Select(e => e.ImmId).Should().Contain([id1, id2, id3]);
    }

    /// <summary>
    /// После удаления устройства <c>GetAll</c> должен возвращать на один элемент
    /// меньше, чем до удаления.
    /// </summary>
    [Fact]
    public void GetAll_AfterRemove_DoesNotContainRemovedEntry()
    {
        var cache = new MemoryImmCache();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        cache.AddImm(id1, 60);
        cache.AddImm(id2, 60);

        cache.RemoveImm(id1);

        cache.GetAll().Should().HaveCount(1)
            .And.NotContain(e => e.ImmId == id1);
    }
}
