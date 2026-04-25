using Wintime.Control.Shared.Settings;

namespace Wintime.Control.Core.Entities;

public class Template : BaseEntity
{
    public string Name { get; set; } = string.Empty; // Наименование/комментарий
    public string Manufacturer { get; set; } = string.Empty; // Производитель
    public string Model { get; set; } = string.Empty; // Модель
    public string Version { get; set; } = "1.0";
    public string Author { get; set; } = string.Empty;
    public string JsonConfig { get; set; } = "{}"; // Конфигурация датчиков
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// Список шаблонов датчиков
    /// </summary>
    public IEnumerable<SensorTemplate> Sensors { get; }  = new List<SensorTemplate>();
    // Navigation
    public ICollection<Imm> Imms { get; set; } = new List<Imm>();
}