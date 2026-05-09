using Wintime.Control.Core.DTOs.Shifts;

namespace Wintime.Control.Core.Interfaces;

public interface IShiftService
{
    Task<IReadOnlyList<ShiftDto>> GetShiftsAsync();
    IReadOnlyList<string> Validate(IReadOnlyList<ShiftScheduleItemDto> shifts);
    Task<IReadOnlyList<ShiftDto>> SaveShiftsAsync(IReadOnlyList<ShiftScheduleItemDto> shifts);
}
