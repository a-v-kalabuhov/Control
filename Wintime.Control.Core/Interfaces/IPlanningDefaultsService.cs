using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Умолчания планирования (PZP-14, ADR-0015). Без кеша: читаются редко.</summary>
public interface IPlanningDefaultsService
{
    Task<PlanningDefaults> GetAsync(CancellationToken ct = default);

    Task<PlanningDefaults> UpdateAsync(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                                       string? userId, CancellationToken ct = default);
}
