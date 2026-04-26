namespace Wintime.Control.Emulator.Models;

public class EmulationPreset
{
    public string ImmId { get; set; } = "";
    public string? ImmName { get; set; }
    public int MessagesPerMinute { get; set; } = 10;
    public List<ProfileStep> Profile { get; set; } = new();
    public List<SensorEmulationConfig> SensorConfigs { get; set; } = new();
    public DateTime? LastModified { get; set; }
}