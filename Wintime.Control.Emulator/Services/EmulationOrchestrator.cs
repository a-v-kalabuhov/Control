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
    private readonly IMqttService _mqtt;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<EmulationOrchestrator> _logger;

    public EmulationOrchestrator(
            IMqttService mqtt, 
            ILoggerFactory loggerFactory,
            ILogger<EmulationOrchestrator> logger)
    {
        _mqtt = mqtt;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task StartAsync(string immId, EmulationRequest request)
    {
        if (_instances.ContainsKey(immId))
            await StopAsync(immId);

        var instanceLogger = _loggerFactory.CreateLogger<ImmEmulationInstance>();

        var instance = new ImmEmulationInstance(immId, request, _mqtt, instanceLogger);
        _instances[immId] = instance;
        instance.Start();
        _logger.LogInformation("Started emulation for {ImmId}", immId);
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
            StartedAt = kvp.Value.StartedAt
        });
    }
}

public class InstanceStatusDto
{
    public string ImmId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime StartedAt { get; set; }
}