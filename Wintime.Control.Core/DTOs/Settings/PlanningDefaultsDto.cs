namespace Wintime.Control.Core.DTOs.Settings;

public class PlanningDefaultsDto
{
    public int SetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
