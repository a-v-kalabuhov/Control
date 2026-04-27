namespace Wintime.Control.Emulator.Models;

/// <summary>
/// DTO для шаблона IMM.
/// Приходит от основнго веб-сервиса.
/// </summary>
public class TemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>
    /// Список датчиков IMM.
    /// </summary>
    public List<SensorConfig> Sensors { get; set; } = new();
}

/// <summary>
/// Шаблон датчика IMM.
/// </summary>
public class SensorConfig
{
    /// <summary>
    /// Имя датчика.
    /// </summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// Тип данных, передаваемых датчиком.
    /// Доступные значения float, boolean, string, cycleCounter.
    /// cycleCounter - целое число, счетчик циклов.
    /// </summary>
    public string Type { get; set; } = "";
}