namespace Wintime.Control.Emulator.Config;

public class StorageSettings
{
    public string PresetsPath { get; set; } = "presets"; // Относительно папки запуска
    
    public string GetAbsolutePresetsPath()
    {
        var path = Path.IsPathRooted(PresetsPath) 
            ? PresetsPath 
            : Path.Combine(AppContext.BaseDirectory, PresetsPath);
        
        Directory.CreateDirectory(path); // Создаём папку, если нет
        return path;
    }
}