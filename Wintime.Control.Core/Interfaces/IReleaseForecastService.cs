using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Прогноз выпуска заданий в работе (BL-32, ADR-0017) по закрытым циклам задания. Один источник
/// для плитки ТПА и план-графика цеха (PZP-07a) — чтобы прогноз совпадал.
/// </summary>
public interface IReleaseForecastService
{
    /// <summary>Прогноз по заданиям из <paramref name="taskIds"/> в статусе InProgress; остальные в ответ не попадают.</summary>
    Task<IReadOnlyDictionary<Guid, ReleaseForecastResult>> CalculateAsync(
        IReadOnlyCollection<Guid> taskIds, DateTime nowUtc, CancellationToken ct = default);
}
