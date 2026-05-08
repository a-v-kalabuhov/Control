using Wintime.Control.Core.Cache;

namespace Wintime.Control.Core.Interfaces;

public interface IImmStatusService
{
    Task UpdateStatusAsync(Guid immId, string newStatus, DateTime changedAt);
    string? GetCurrentStatus(Guid immId);
    IReadOnlyList<ImmStatusEntry> GetAllStatuses();
}
