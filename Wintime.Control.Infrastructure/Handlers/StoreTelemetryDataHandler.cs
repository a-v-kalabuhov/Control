using Microsoft.Extensions.Logging;
using System.Globalization;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Handlers;

public class StoreTelemetryDataHandler : IStoreTelemetryDataHandler
{
    private readonly ControlDbContext _dbContext;
    private readonly ILogger<StoreTelemetryDataHandler> _logger;

    public StoreTelemetryDataHandler(ControlDbContext dbContext, ILogger<StoreTelemetryDataHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<bool> SaveAsync(MqttProcessingContext context)
    {
        var data = context.Data!;
        var immId = context.Device!.Id;
        var timestamp = data.TimestampUtc;

        var sensorsByName = context.Template!.Sensors.ToDictionary(s => s.ParameterName);

        var entries = new List<Telemetry>(data.Sensors.Count);

        foreach (var (name, sv) in data.Sensors)
        {
            var entry = new Telemetry
            {
                ImmId = immId,
                Timestamp = timestamp,
                ParameterName = name
            };

            if (name == "cycleCounter")
            {
                entry.ValueNumeric = decimal.TryParse(sv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numC)
                    ? numC
                    : null;
                if (entry.ValueNumeric is null)
                    entry.ValueText = sv.Value;
            }
            else if (sensorsByName.TryGetValue(name, out var sensorTemplate))
            {
                switch (sensorTemplate.ParameterType)
                {
                    case "float":
                        if (decimal.TryParse(sv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numF))
                            entry.ValueNumeric = numF;
                        else
                            entry.ValueText = sv.Value;
                        break;
                    case "int":
                    case "cycleCounter":
                    case "injectionDuration":
                    case "cyclePause":
                        if (decimal.TryParse(sv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numI))
                            entry.ValueNumeric = numI;
                        else
                            entry.ValueText = sv.Value;
                        break;
                    default: // string, boolean
                        entry.ValueText = sv.Value;
                        break;
                }
            }
            else
            {
                entry.ValueText = sv.Value; // неизвестный датчик — сохраняем как текст
            }

            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            _logger.LogWarning("IMM {ImmId}: no sensor readings to save, message {MessageId}", immId, context.MessageId);
            return false;
        }

        _dbContext.Telemetry.AddRange(entries);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("IMM {ImmId}: saved {Count} telemetry rows at {Timestamp:O}", immId, entries.Count, timestamp);

        return true;
    }
}
