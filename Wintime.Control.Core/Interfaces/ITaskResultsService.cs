using Wintime.Control.Core.DTOs.Tasks;

namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Итоги выполнения задания (вкладка «Результаты выполнения»). Одна точка расчёта —
/// на ней же строится отчёт по заданиям (BL-05).
/// </summary>
public interface ITaskResultsService
{
    /// <summary>Итоги на «сейчас»; null — задания нет.</summary>
    Task<TaskResultsDto?> GetAsync(Guid taskId, CancellationToken ct = default);
}
