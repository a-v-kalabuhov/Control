namespace Wintime.Control.Emulator.Config;

/// <summary>
/// Настройки хранилища.
/// </summary>
public class StorageSettings
{
    /// <summary>
    /// Путь к папке с пресетами.
    /// Каждый пресет хранится в отдельнйо файле в этой папке.
    /// Указывается относительно папки запуска.
    /// </summary>
    public string PresetsPath { get; set; } = "presets";
    
    public string GetAbsolutePresetsPath()
    {
        var path = Path.IsPathRooted(PresetsPath) 
            ? PresetsPath 
            : Path.Combine(AppContext.BaseDirectory, PresetsPath);
        
        Directory.CreateDirectory(path); // Создаём папку, если нет
        return path;
    }
}