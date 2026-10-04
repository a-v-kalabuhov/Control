using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Входные ряды <see cref="EffectiveStatusTimeline.Build"/> для одного ТПА.</summary>
public record EffectiveStatusInputs(
    IReadOnlyList<RawSegment> Raw,
    IReadOnlyList<TaskInterval> Tasks,
    IReadOnlyList<Interval> Downtimes);

/// <summary>
/// Сбор историзированных рядов (сырой статус, интервалы заданий, простои) для реконструкции
/// эффективного состояния. Используется дашбордом ТПА и отчётами.
/// </summary>
public interface IEffectiveStatusHistoryService
{
    /// <summary>
    /// Входы за окно [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) для нескольких ТПА тремя
    /// запросами. Открытые интервалы обрезаются по <paramref name="effectiveTo"/> (обычно min(to, now)).
    /// В результате есть ключ для каждого id из <paramref name="immIds"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, EffectiveStatusInputs>> GatherAsync(
        IReadOnlyCollection<Guid> immIds, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo,
        CancellationToken ct = default);
}
