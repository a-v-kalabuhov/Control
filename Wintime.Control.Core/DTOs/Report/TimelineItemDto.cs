namespace Wintime.Control.Core.DTOs.Report;

public class TimelineItemDto
{
    public DateTime Start { get; set; }
    public DateTime End   { get; set; }
    /// <summary>Ключ эффективного состояния: Production | Setup | Downtime | Unplanned | NoTask | Offline | NoData.</summary>
    public string Type    { get; set; } = string.Empty;
}
