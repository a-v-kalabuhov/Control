using System.Collections.Concurrent;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Cache;

public sealed class MemoryImmCache : IImmCache
{
    private readonly ConcurrentDictionary<Guid, ImmCacheEntry> _cache = new();

    public ImmCacheEntry? GetEntry(Guid immId)
        => _cache.TryGetValue(immId, out var entry) ? entry : null;

    public void AddImm(Guid immId, int timeoutSeconds)
        => _cache.TryAdd(immId, new ImmCacheEntry(
            immId,
            DateTime.MinValue,
            timeoutSeconds,
            new Dictionary<string, string>()));

    public void RemoveImm(Guid immId)
        => _cache.TryRemove(immId, out _);

    public void UpdateEntry(Guid immId, DateTime messageAt, int timeoutSeconds, IReadOnlyDictionary<string, string> sensorValues)
        => _cache.AddOrUpdate(
            immId,
            _ => new ImmCacheEntry(immId, messageAt, timeoutSeconds, sensorValues),
            (_, existing) => existing with { LastMessageAt = messageAt, SensorValues = sensorValues });

    public IReadOnlyList<ImmCacheEntry> GetAll()
        => _cache.Values.ToList();
}
