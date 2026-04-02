namespace Wintime.Control.Core.DTOs.Downtime;

public class DowntimeReasonDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Planned, Emergency
    public bool IsActive { get; set; }
}