namespace Wintime.Control.Core.DTOs.Downtime;

public class UpdateDowntimeReasonRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
