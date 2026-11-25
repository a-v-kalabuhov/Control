using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftPlanMaterializer
{
    Task<ShiftPlanDay> MaterializeAsync(DateOnly date, int position, CancellationToken ct);

    /// <summary>Загружает день для изменения внутри транзакции под ShiftScheduleLock.</summary>
    Task<ShiftPlanDay> LoadTrackedDayForUpdateAsync(DateOnly date, CancellationToken ct);
}
