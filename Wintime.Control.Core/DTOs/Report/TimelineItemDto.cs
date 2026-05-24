namespace Wintime.Control.Core.DTOs.Report;

public class TimelineItemDto
{
    public DateTime Start { get; set; }
    public DateTime End   { get; set; }
    /// <summary>work | setup | alarm | idle | offline</summary>
    public string Type    { get; set; } = string.Empty;
}
