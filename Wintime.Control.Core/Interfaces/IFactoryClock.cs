using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Часы и расписание завода: текущее время и данные для планового начала заданий.</summary>
public interface IFactoryClock
{
    DateTime UtcNow { get; }

    /// <summary>Пояс завода и начало самой ранней смены. Кешируется на время запроса (scoped).</summary>
    Task<FactorySchedule> GetScheduleAsync(CancellationToken ct = default);
}
