namespace Wintime.Control.Emulator.Services;

using Wintime.Control.Emulator.Models;


public class ImmEmulationInstance : IAsyncDisposable
{
    private readonly string _immId;
    private readonly EmulationRequest _request;
    private readonly IMqttService _mqtt;
    private readonly ILogger<ImmEmulationInstance> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, ISignalGenerator> _generators = [];
    private int _counter = 0;
    private Task? _runTask;
    private bool _disposed;

    public string Status { get; private set; } = "Stopped";
    public DateTime StartedAt { get; private set; }

    public ImmEmulationInstance(
        string immId,
        EmulationRequest request,
        IMqttService mqtt,
        ILogger<ImmEmulationInstance> logger)
    {
        _immId = immId;
        _request = request;
        _mqtt = mqtt;
        _logger = logger;

        foreach (var cfg in request.SensorConfigs)
        {
            _generators[cfg.Name] = cfg.Type switch
            {
                "float" => new FloatSignalGenerator(cfg),
                "boolean" => new BooleanSignalGenerator(cfg),
                "string" => new StringSignalGenerator(cfg),
                _ => throw new ArgumentException($"Unknown type: {cfg.Type}")
            };
        }
    }

    public void Start()
    {
        if (_runTask != null) 
            return;
        Status = "Running";
        StartedAt = DateTime.UtcNow;
        _runTask = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        if (_runTask != null)
            await _runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Status = "Stopped";
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            var intervalMs = 60000 / Math.Max(_request.MessagesPerMinute, 1);
            var profileIndex = 0;
            var stepStart = DateTime.UtcNow;

            while (!ct.IsCancellationRequested)
            {
                var step = _request.Profile[profileIndex];
                var elapsed = (DateTime.UtcNow - stepStart).TotalSeconds;

                if (elapsed >= step.DurationSeconds)
                {
                    profileIndex = (profileIndex + 1) % _request.Profile.Count;
                    stepStart = DateTime.UtcNow;
                    step = _request.Profile[profileIndex];
                    // Increment counter on mode change to 'auto' if needed, 
                    // but spec says counter increments on cycle end (mold open).
                    if (step.Mode == "auto")
                        _counter++;
                }

                var payload = new TelemetryPayload
                {
                    Timestamp = DateTime.UtcNow,
                    Sensors = new Dictionary<string, object>
                    {
                        ["counter"] = _counter,
                        ["mode"] = step.Mode
                    }
                };

                foreach (var gen in _generators)
                {
                    payload.Sensors[gen.Key] = gen.Value.GenerateValue(step.Mode);
                }

                await _mqtt.PublishAsync(_immId, payload, ct);
                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Emulation error for {ImmId}", _immId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        _cts.Dispose();
        _disposed = true;
    }
}