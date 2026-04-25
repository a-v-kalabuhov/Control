namespace Wintime.Control.Emulator.Models;

public class TemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<SensorConfig> Sensors { get; set; } = new();
}

public class SensorConfig
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // float, boolean, string, cycleCounter
}