namespace Wintime.Control.Core.Entities;

public class DowntimeReason : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Planned"; // Planned, Emergency
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Event> Events { get; set; } = new List<Event>();
}