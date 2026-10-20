using Wintime.Control.Core.DTOs.Planning;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Конфликты плана (PZP-07b, ADR-0022): все незавершённые задания, отрезки (<c>TaskOccupancy</c>), порог из
/// <c>PlanningDefaults</c>. Один источник для бейджа, баннера, доски, карточки и списка.
/// </summary>
public interface IPlanConflictService
{
    Task<PlanConflictsDto> GetAllAsync(DateTime nowUtc, CancellationToken ct = default);
}
