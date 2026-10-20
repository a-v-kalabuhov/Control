namespace Wintime.Control.Core.DTOs.Settings;

public class UpdatePlanningDefaultsRequestDto
{
    public int SetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }

    /// <summary>Не передан — без изменений.</summary>
    public int? ConflictThresholdMinutes { get; set; }
}
