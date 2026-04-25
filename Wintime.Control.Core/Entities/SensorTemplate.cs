namespace Wintime.Control.Core.Entities;
public class SensorTemplate
{
    public string ParameterName { get; set; } = string.Empty;
    public string ParameterType { get; set; } = "numeric"; // numeric, discrete, string
    public decimal Threshold { get; set; } = 0;
    public int TimeoutSeconds { get; set; } = 300;
    public List<string>? AllowedValues { get; set; } // Для дискретных значений
}