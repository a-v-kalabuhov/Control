namespace Wintime.Control.Core.DTOs.Template;

public class CreateTemplateRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public object JsonConfig { get; set; } = new();
}