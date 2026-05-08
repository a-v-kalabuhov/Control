using System.Collections.Concurrent;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Cache;

/// <summary>
/// Потокобезопасная in-memory реализация <see cref="IImmStatusCache"/>
/// на основе <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
/// <remarks>
/// Регистрируется как singleton. Заполняется при старте приложения через
/// <c>ImmStatusStartupService</c> и обновляется в процессе работы сервисом
/// <see cref="Services.ImmStatusService"/>.
/// </remarks>
public class MemoryImmStatusCache : IImmStatusCache
{
    private readonly ConcurrentDictionary<Guid, ImmStatusEntry> _cache = new();

    /// <summary>
    /// Возвращает текущий статус ТПА.
    /// </summary>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <returns>Строка статуса или <see langword="null"/>, если ТПА не найден в кеше.</returns>
    public string? GetStatus(Guid immId)
        => _cache.TryGetValue(immId, out var entry) ? entry.Status : null;

    public ImmStatusEntry? GetEntry(Guid immId)
        => _cache.TryGetValue(immId, out var entry) ? entry : null;

    /// <summary>
    /// Устанавливает или перезаписывает статус ТПА в кеше.
    /// </summary>
    /// <param name="immId">Идентификатор ТПА.</param>
    /// <param name="status">Новый статус (например, <c>"Online"</c>, <c>"Offline"</c>).</param>
    /// <param name="sinceUtc">Момент перехода в данный статус (UTC).</param>
    public void SetStatus(Guid immId, string status, DateTime sinceUtc)
        => _cache[immId] = new ImmStatusEntry(immId, status, sinceUtc);

    /// <summary>
    /// Возвращает снимок текущих статусов всех ТПА.
    /// </summary>
    /// <returns>Неизменяемый список записей <see cref="ImmStatusEntry"/>.</returns>
    public IReadOnlyList<ImmStatusEntry> GetAll()
        => _cache.Values.ToList();
}
