using System.Text.Json;

namespace Wintime.Control.Emulator.Models;

/// <summary>
/// DTO для шаблона IMM.
/// Приходит от основнго веб-сервиса.
/// </summary>
public class TemplateDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string JsonConfig { get; set; } = "";
    /// <summary>
    /// Список датчиков IMM.
    /// </summary>
    public List<SensorConfig> Sensors { get; set; } = new();
    public void UpdateSensors()
    {
        Sensors.Clear();
        if (string.IsNullOrWhiteSpace(JsonConfig))
            return;
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var wrapper = JsonSerializer.Deserialize<SensorConfigJson>(JsonConfig, options);
        if (wrapper == null)
            return;
        // Теперь создаём список нужных объектов
        Sensors = wrapper.Sensors
            .Select(s => new SensorConfig
                {
                    Name = s.name,
                    Type = s.type
                })
                .ToList();
    }
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

public class SensorConfigJson
{
    public List<SensorJson> Sensors { get; set; } = new();
}

public class SensorJson
{
    public string name { get; set; } = string.Empty;
    public string field { get; set; } = string.Empty;
    public string type { get; set; } = string.Empty;
    public double? threshold { get; set; }
}