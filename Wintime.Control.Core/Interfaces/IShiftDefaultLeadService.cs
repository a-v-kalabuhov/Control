using Wintime.Control.Core.DTOs.Shifts;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftDefaultLeadService
{
    Task<ShiftDefaultLeadSettingsDto> GetAsync(CancellationToken ct);
    Task<ShiftDefaultLeadSettingsDto> SaveAsync(
        SaveShiftDefaultLeadsRequestDto request, CancellationToken ct);

    /// <summary>Вызывается внутри транзакции и блокировки замены графика; SaveChanges выполняет вызывающий код.</summary>
    Task SynchronizeWithScheduleAsync(int positionCount, CancellationToken ct);
}
