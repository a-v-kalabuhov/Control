using Wintime.Control.Core.Cache;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Tests.Unit.Helpers;

/// <summary>
/// Фабричные методы для создания тестовых данных MQTT pipeline.
/// </summary>
internal static class PipelineTestFixtures
{
    public static MqttProcessingContext MakeContext(
        string topic,
        string payload,
        MqttTelemetryMessage? data = null,
        ImmDto? device = null,
        CachedTemplate? template = null)
        => new(Guid.NewGuid(), topic, payload, data, device, template);

    public static MqttTelemetryMessage MakeMessage(
        Guid immId,
        Dictionary<string, SignalValue>? sensors = null,
        string? mode = "auto",
        DateTime? timestampUtc = null,
        CycleSnapshot? currentCycle = null,
        CompletedCycleSnapshot? lastCycle = null)
        => new()
        {
            TimestampUtc = timestampUtc ?? DateTime.UtcNow,
            DeviceId = immId.ToString(),
            Mode = mode,
            Sensors = sensors ?? [],
            CurrentCycle = currentCycle,
            LastCycle = lastCycle
        };

    public static Dictionary<string, SignalValue> MakeSensors(params (string Name, string Value)[] values)
    {
        var dict = new Dictionary<string, SignalValue>();
        foreach (var (name, value) in values)
            dict[name] = new SignalValue(value, Error: false);
        if (!dict.ContainsKey("cycleCounter"))
            dict["cycleCounter"] = new SignalValue("0", Error: false);
        return dict;
    }

    public static CachedTemplate MakeTemplate(
        IReadOnlyList<SensorTemplate>? sensors = null,
        int timeoutSeconds = 60)
        => new(
            Guid.NewGuid(),
            "Test Template",
            DateTime.UtcNow,
            timeoutSeconds,
            sensors ?? []);

    public static SensorTemplate MakeSensor(
        string name,
        string type = "float",
        decimal threshold = 0,
        bool required = false,
        IReadOnlyList<string>? allowedValues = null)
        => new(name, name, type, threshold, allowedValues, required);

    public static ImmDto MakeImmDto(Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Test IMM",
            TemplateId = Guid.NewGuid(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    public static ImmCacheEntry MakeImmCacheEntry(
        Guid immId,
        DateTime lastMessageAt,
        IReadOnlyDictionary<string, string>? sensorValues = null,
        int timeoutSeconds = 60)
        => new(immId, lastMessageAt, timeoutSeconds, sensorValues ?? new Dictionary<string, string>());
}
