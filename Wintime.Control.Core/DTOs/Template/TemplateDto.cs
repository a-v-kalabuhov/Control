namespace Wintime.Control.Core.DTOs.Template;

public class TemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string Version { get; set; } = "1.0";
    public string? Author { get; set; }
    public DateTime CreatedAt { get; set; }
    public int SensorCount { get; set; }
}