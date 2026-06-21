using System.Collections.Concurrent;
using Wintime.Control.Emulator.Models;

namespace Wintime.Control.Emulator.Services;

/// <summary>
/// IMM emulation instances orchestrator.
/// </summary>
public class EmulationOrchestrator
{
    /// <summary>
    /// Список инстансов эмуляции IMM.
    /// Ключом является ID IMM.
    /// </summary>
    private readonly ConcurrentDictionary<string, ImmEmulationInstance> _instances = new();
    private readonly IEmulatorMqttService _mqtt;
    private readonly IPresetStorage _presetStorage;
    private readonly ILogger<EmulationOrchestrator> _logger;

    public EmulationOrchestrator(
            IEmulatorMqttService mqtt,
            IPresetStorage presetStorage,
            ILogger<EmulationOrchestrator> logger)
    {
        _mqtt = mqtt;
        _presetStorage = presetStorage;
        _logger = logger;
    }

    public async Task StartAsync(string immId, EmulationRequest request, InstanceMode initialMode = InstanceMode.Idle)
    {
        if (_instances.ContainsKey(immId))
            await StopAsync(immId);

        var instance = new ImmEmulationInstance(immId, request, _mqtt, initialMode);
        _instances[immId] = instance;
        instance.Start();
        _logger.LogInformation("Started emulation for {ImmId} in {Mode} mode", immId, initialMode);
    }

    /// <summary>
    /// Запускает все настроенные инстансы IMM в режиме Idle.
    /// Настроенный инстанс — тот, у которого есть пресет с шагами и хотя бы одним ненулевым числовым датчиком.
    /// </summary>
    public async Task StartAllAsync(IEnumerable<string> immIds, CancellationToken ct)
    {
        foreach (var immId in immIds)
        {
            if (ct.IsCancellationRequested) break;

            var preset = await _presetStorage.LoadAsync(immId, ct);
            if (!IsConfigured(preset))
            {
                _logger.LogDebug("Skipping {ImmId}: preset not configured", immId);
                continue;
            }

            var request = new EmulationRequest
            {
                ImmId = preset!.ImmId,
                Profile = preset.Profile,
                MessagesPerMinute = preset.MessagesPerMinute,
                SensorConfigs = preset.SensorConfigs
            };

            await StartAsync(immId, request, InstanceMode.Idle);
        }
    }

    public void SetInstanceMode(string immId, InstanceMode mode)
    {
        if (_instances.TryGetValue(immId, out var instance))
        {
            instance.SetMode(mode);
            _logger.LogInformation("IMM {ImmId} mode changed to {Mode}", immId, mode);
        }
        else
        {
            _logger.LogWarning("SetMode: instance {ImmId} not found", immId);
        }
    }

    public async Task StopAsync(string immId)
    {
        if (_instances.TryRemove(immId, out var instance))
        {
            await instance.StopAsync();
            _logger.LogInformation("Stopped emulation for {ImmId}", immId);
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var instance in _instances.Values)
            await instance.StopAsync();
        _instances.Clear();
    }

    public IEnumerable<InstanceStatusDto> GetStatuses()
    {
        return _instances.Select(kvp => new InstanceStatusDto
        {
            ImmId = kvp.Key,
            Status = kvp.Value.Status,
            Mode = kvp.Value.Mode.ToString().ToLowerInvariant(),
            StartedAt = kvp.Value.StartedAt,
            RecentMessages = kvp.Value.RecentMessages.ToList()
        });
    }

    private static bool IsConfigured(EmulationPreset? preset)
    {
        if (preset is null || preset.Profile.Count == 0)
            return false;

        return preset.SensorConfigs.Any(s =>
            (s.Type is "float" && (s.BaseValueAuto != 0 || s.BaseValueManual != 0 || s.BaseValueIdle != 0)) ||
            (s.Type is "int" or "integer" && (s.IntBaseValueAuto != 0 || s.IntBaseValueManual != 0 || s.IntBaseValueIdle != 0)));
    }
}

public class InstanceStatusDto
{
    public string ImmId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public List<MessageLogEntry> RecentMessages { get; set; } = [];
}

public class MessageLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Mode { get; set; } = "";
}
