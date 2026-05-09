namespace Wintime.Control.Core.DTOs.Shifts;

public class ShiftDto
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public int StartMinutes { get; set; }
    public int DurationMinutes { get; set; }
    public int BreakStartMinutes { get; set; }
    public int BreakDurationMinutes { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string? BreakStartTime { get; set; }
    public string? BreakEndTime { get; set; }
}
