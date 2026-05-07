namespace Wintime.Control.Core.Entities;

public sealed record SensorTemplate(
    string Name,
    string ParameterName,
    string ParameterType,
    decimal Threshold,
    IReadOnlyList<string>? AllowedValues = null
);
