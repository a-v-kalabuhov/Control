namespace Wintime.Control.Core.DTOs.Downtime;

public class UpdateDowntimeEventRequestDto
{
    public Guid? ReasonId { get; set; }
    public DateTime? EndTime { get; set; }
    public string? Comment { get; set; }
}
