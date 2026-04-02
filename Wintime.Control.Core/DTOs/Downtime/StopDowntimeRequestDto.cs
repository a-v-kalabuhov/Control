namespace Wintime.Control.Core.DTOs.Downtime;

public class StopDowntimeRequestDto
{
    public Guid ImmId { get; set; }
    public DateTime? EndTime { get; set; }
}