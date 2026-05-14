using System.Collections.Concurrent;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Services;

public class CycleTracker : ICycleTracker
{
    private readonly ConcurrentDictionary<Guid, CycleState> _states = new();

    public CycleState? Get(Guid immId) =>
        _states.TryGetValue(immId, out var state) ? state : null;

    public void Set(Guid immId, CycleState state) =>
        _states[immId] = state;
}
