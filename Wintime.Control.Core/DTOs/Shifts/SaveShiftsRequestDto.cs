namespace Wintime.Control.Core.DTOs.Shifts;

public class SaveShiftsRequestDto
{
    public List<ShiftScheduleItemDto> Shifts { get; set; } = new();
}

public class ShiftScheduleItemDto
{
    public int StartMinutes { get; set; }
    public int DurationMinutes { get; set; }
    public int BreakStartMinutes { get; set; }
    public int BreakDurationMinutes { get; set; }
}
