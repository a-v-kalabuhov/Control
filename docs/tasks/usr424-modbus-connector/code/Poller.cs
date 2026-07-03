namespace UsrIoPoller;

/// <summary>Снимок значений всех тегов за один опрос (stateless-результат).</summary>
public sealed record TagSnapshot(DateTime TimestampUtc, IReadOnlyDictionary<string, TagSample> Tags);

/// <summary>
/// Приёмник снимков с состоянием (детекция цикла, дельты, усреднение, привязка к фазе).
/// Именно здесь живёт вся stateful-логика — вне конфига и вне декодера.
/// </summary>
public interface ITagProcessor
{
    void Process(TagSnapshot snapshot);
}

/// <summary>
/// Периодически опрашивает устройство, декодирует теги и раздаёт снимки процессорам.
/// Чтение последовательное; при ошибке — переподключение с backoff.
/// </summary>
public sealed class Poller
{
    private readonly PollingConfig _config;
    private readonly Func<IModbusReader> _readerFactory;
    private readonly IReadOnlyList<ReadBlock> _blocks;
    private readonly List<ITagProcessor> _processors = [];

    private IModbusReader? _reader;

    /// <summary>Последний успешный снимок (для внешнего доступа/UI).</summary>
    public TagSnapshot? Latest { get; private set; }

    /// <summary>Событие на каждый успешный снимок.</summary>
    public event Action<TagSnapshot>? SnapshotReady;

    public Poller(PollingConfig config, Func<IModbusReader> readerFactory)
    {
        _config = config;
        _readerFactory = readerFactory;
        _blocks = BatchPlanner.Plan(config.Registers, config.Device.MaxBatchGap);
    }

    public Poller AddProcessor(ITagProcessor processor)
    {
        _processors.Add(processor);
        return this;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.Device.PollIntervalMs));
        int backoffMs = 0;

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                EnsureConnected();
                var snapshot = PollOnce();
                Latest = snapshot;
                backoffMs = 0;

                SnapshotReady?.Invoke(snapshot);
                foreach (var p in _processors)
                    p.Process(snapshot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Рвём соединение — следующий тик переподключится.
                _reader?.Dispose();
                _reader = null;

                Console.Error.WriteLine($"[poll] ошибка: {ex.Message}");

                // Небольшой backoff, чтобы не молотить переподключениями при обрыве.
                backoffMs = Math.Min(backoffMs == 0 ? _config.Device.PollIntervalMs : backoffMs * 2, 5000);
                try { await Task.Delay(backoffMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }
        }
    }

    private void EnsureConnected() => _reader ??= _readerFactory();

    private TagSnapshot PollOnce()
    {
        var reader = _reader!;
        byte unit = _config.Device.UnitId;
        var ts = DateTime.UtcNow;
        var tags = new Dictionary<string, TagSample>(_config.Registers.Count);

        foreach (var block in _blocks)
        {
            if (block.Access is ModbusAccess.Coil or ModbusAccess.DiscreteInput)
            {
                bool[] bits = block.Access == ModbusAccess.Coil
                    ? reader.ReadCoils(unit, block.Start, block.Quantity)
                    : reader.ReadDiscreteInputs(unit, block.Start, block.Quantity);

                // Метку времени берём после ответа устройства.
                ts = DateTime.UtcNow;
                foreach (var def in block.Members)
                {
                    int idx = def.Address - block.Start;
                    tags[def.Name] = TagDecoder.FromBit(def, bits[idx], ts);
                }
            }
            else
            {
                ushort[] words = block.Access == ModbusAccess.HoldingRegister
                    ? reader.ReadHoldingRegisters(unit, block.Start, block.Quantity)
                    : reader.ReadInputRegisters(unit, block.Start, block.Quantity);

                ts = DateTime.UtcNow;
                foreach (var def in block.Members)
                {
                    int idx = def.Address - block.Start;
                    tags[def.Name] = TagDecoder.FromRegisters(def, words.AsSpan(idx, def.Count), ts);
                }
            }
        }

        return new TagSnapshot(ts, tags);
    }
}
