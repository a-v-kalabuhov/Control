namespace Wintime.Control.Core.Entities;

public class Shift : BaseEntity
{
    public int StartMinutes { get; set; }
    public int DurationMinutes { get; set; }
    public int BreakStartMinutes { get; set; }
    public int BreakDurationMinutes { get; set; }
}
