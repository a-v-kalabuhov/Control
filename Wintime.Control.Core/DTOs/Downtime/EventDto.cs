namespace Wintime.Control.Core.DTOs.Downtime;

public class EventDto
{
    public Guid Id { get; set; }
    public Guid ImmId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid? ReasonId { get; set; }
    public string? ReasonName { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public string? PersonnelId { get; set; }
}