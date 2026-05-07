namespace Wintime.Control.Core.Entities;

public class Template : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = string.Empty;
    public string JsonConfig { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Imm> Imms { get; set; } = new List<Imm>();
}
