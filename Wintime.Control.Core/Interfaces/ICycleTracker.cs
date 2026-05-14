namespace Wintime.Control.Core.Interfaces;

public record CycleState(DateTime? CycleStartTime, int? LastCounterValue, string? LastMode);

/// <summary>
/// In-memory хранилище состояния отслеживания циклов по каждому ТПА.
/// </summary>
public interface ICycleTracker
{
    CycleState? Get(Guid immId);
    void Set(Guid immId, CycleState state);
}
