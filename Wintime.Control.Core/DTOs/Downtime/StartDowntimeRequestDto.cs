namespace Wintime.Control.Core.DTOs.Downtime;

public class StartDowntimeRequestDto
{
    public Guid ImmId { get; set; }
    public Guid ReasonId { get; set; }
    public string? PersonnelId { get; set; }
    public DateTime? StartTime { get; set; }
}