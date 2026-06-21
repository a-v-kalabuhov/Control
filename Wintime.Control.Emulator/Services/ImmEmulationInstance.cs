namespace Wintime.Control.Emulator.Services;

using Wintime.Control.Emulator.Models;

/// <summary>
/// Класс для эмуляции IMM.
/// Эмуляция заключается в генерации данных для датчиков, описанных в конфиге, и отправке сгенерированных данных по MQTT.
/// Бесконечно выполняет метод RunLoopAsync, в котором и происходит генерация данных.
/// Режим инстанса (InstanceMode) управляется извне через SetMode.
/// </summary>
public class ImmEmulationInstance : IAsyncDisposable
{
    private readonly string _immId;
    private readonly EmulationRequest _request;
    private readonly IEmulatorMqttService _mqtt;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, ISignalGenerator> _generators = [];
    private string? _cycleCounterSensorName;
    private int _counter = 0;
    private Task? _runTask;
    private bool _disposed;

    private volatile InstanceMode _mode;
    private volatile TaskCompletionSource _modeChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly object _logLock = new();
    private readonly List<MessageLogEntry> _recentMessages = new(5);

    public string Status { get; private set; } = "Stopped";
    public DateTime StartedAt { get; private set; }
    public InstanceMode Mode => _mode;

    public IReadOnlyList<MessageLogEntry> RecentMessages
    {
        get { lock (_logLock) return _recentMessages.ToList(); }
    }

    public ImmEmulationInstance(
        string immId,
        EmulationRequest request,
        IEmulatorMqttService mqtt,
        InstanceMode initialMode = InstanceMode.Auto)
    {
        _immId = immId;
        _request = request;
        _mqtt = mqtt;
        _mode = initialMode;

        foreach (var cfg in request.SensorConfigs)
        {
            var mqttKey = string.IsNullOrEmpty(cfg.Field) ? cfg.Name : cfg.Field;
            if (cfg.Type == "cycleCounter")
            {
                _cycleCounterSensorName = mqttKey;
                continue;
            }

            _generators[mqttKey] = cfg.Type switch
            {
                "float" => new FloatSignalGenerator(cfg),
                "int" or "integer" => new IntSignalGenerator(cfg),
                "bool" or "boolean" => new BooleanSignalGenerator(cfg),
                "string" => new StringSignalGenerator(cfg),
                _ => throw new ArgumentException($"Unknown type: {cfg.Type}")
            };
        }
    }

    /// <summary>
    /// Меняет режим инстанса. Немедленно прерывает текущую фазу выполнения.
    /// </summary>
    public void SetMode(InstanceMode mode)
    {
        _mode = mode;
        var old = Interlocked.Exchange(ref _modeChanged,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult();
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
        {
            try { await _runTask.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
        Status = "Stopped";
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var intervalMs = 60000 / Math.Max(_request.MessagesPerMinute, 1);

        // Отправка вынесена в очередь EmulatorMqttService и больше не бросает сетевых
        // исключений — здесь остаётся только переключение между режимами и штатная
        // остановка по CancellationToken.
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var currentMode = _mode;

                if (currentMode == InstanceMode.Auto)
                    await RunAutoLoopAsync(intervalMs, ct);
                else
                    await RunFlatLoopAsync(currentMode, intervalMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // штатная остановка
        }
    }

    /// <summary>
    /// Генерирует сигналы для фиксированного режима (Idle или Manual) до смены InstanceMode.
    /// </summary>
    private async Task RunFlatLoopAsync(InstanceMode mode, int intervalMs, CancellationToken ct)
    {
        var mqttMode = mode == InstanceMode.Idle ? "idle" : "manual";

        while (!ct.IsCancellationRequested && _mode == mode)
        {
            await _mqtt.PublishAsync(_immId, BuildPayload(mqttMode), ct);
            RecordMessage(mqttMode);

            var modeChangedTask = _modeChanged.Task;
            await Task.WhenAny(Task.Delay(intervalMs, ct), modeChangedTask);
        }
    }

    /// <summary>
    /// Циклически выполняет шаги пресета. Поле mode в MQTT берётся из шага пресета.
    /// Прерывается при смене InstanceMode.
    /// </summary>
    private async Task RunAutoLoopAsync(int intervalMs, CancellationToken ct)
    {
        var profileIndex = 0;
        var firstStep = true;

        while (!ct.IsCancellationRequested && _mode == InstanceMode.Auto)
        {
            var step = _request.Profile[profileIndex];
            var stepEnd = DateTime.UtcNow.AddSeconds(step.DurationSeconds);

            if (!firstStep && step.Mode == "auto")
                _counter++;
            firstStep = false;

            while (DateTime.UtcNow < stepEnd && !ct.IsCancellationRequested && _mode == InstanceMode.Auto)
            {
                await _mqtt.PublishAsync(_immId, BuildPayload(step.Mode), ct);
                RecordMessage(step.Mode);

                var modeChangedTask = _modeChanged.Task;
                var remainingMs = (int)(stepEnd - DateTime.UtcNow).TotalMilliseconds;
                await Task.WhenAny(Task.Delay(Math.Min(intervalMs, Math.Max(remainingMs, 0)), ct), modeChangedTask);
            }

            profileIndex = (profileIndex + 1) % _request.Profile.Count;
        }
    }

    private void RecordMessage(string mode)
    {
        lock (_logLock)
        {
            _recentMessages.Insert(0, new MessageLogEntry { Timestamp = DateTime.UtcNow, Mode = mode });
            if (_recentMessages.Count > 5)
                _recentMessages.RemoveAt(5);
        }
    }

    private TelemetryPayload BuildPayload(string mode)
    {
        var payload = new TelemetryPayload
        {
            Timestamp = DateTime.UtcNow,
            Mode = mode,
            Sensors = []
        };

        if (_cycleCounterSensorName != null)
            payload.Sensors[_cycleCounterSensorName] = _counter;

        foreach (var gen in _generators)
            payload.Sensors[gen.Key] = gen.Value.GenerateValue(mode);

        return payload;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        _cts.Dispose();
        _disposed = true;
    }
}
