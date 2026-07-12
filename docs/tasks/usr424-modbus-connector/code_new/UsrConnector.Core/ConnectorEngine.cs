namespace UsrConnector.Core;

/// <summary>
/// Движок нижнего слоя коннектора. На каждый тик периода опроса:
/// читает устройство → декодирует → сводит в RoleSnapshot → прогоняет через автомат →
/// эмитит <see cref="MachineState"/>. При неудачном чтении подаёт в автомат снимок
/// Disconnected (offline решает автомат по порогу подряд неудачных опросов).
///
/// Верхний слой (Host) подписывается на <see cref="StateUpdated"/> и решает, что делать
/// с состоянием (консоль, MQTT, REST). В режиме Offline верхний слой сообщения наружу
/// не отправляет — по контракту.
/// </summary>
public sealed class ConnectorEngine
{
    private readonly ConnectorConfig _config;
    private readonly Func<IModbusReader> _readerFactory;
    private readonly IReadOnlyList<ReadBlock> _blocks;
    private readonly MachineStateMachine _fsm;

    private IModbusReader? _reader;

    /// <summary>Последнее вычисленное состояние.</summary>
    public MachineState? Latest { get; private set; }

    /// <summary>Событие на каждый опрос (успешный или нет) с актуальным состоянием.</summary>
    public event Action<MachineState>? StateUpdated;

    public ConnectorEngine(ConnectorConfig config, Func<IModbusReader> readerFactory,
        DateTimeOffset? startUtc = null)
    {
        ConnectorProfileValidator.Validate(config);
        _config = config;
        _readerFactory = readerFactory;
        _blocks = BatchPlanner.Plan(config.Registers, config.Device.MaxBatchGap);
        _fsm = new MachineStateMachine(config.StateMachine, startUtc ?? DateTimeOffset.UtcNow);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.Device.PollIntervalMs));

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            RoleSnapshot snapshot;
            try
            {
                _reader ??= _readerFactory();
                var samples = PollOnce(_reader);
                snapshot = RoleMapper.Map(samples, DateTimeOffset.UtcNow);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Любая ошибка чтения/подключения = неудачный опрос. Соединение рвём,
                // следующий тик попробует переподключиться. Offline решает автомат.
                _reader?.Dispose();
                _reader = null;
                snapshot = RoleSnapshot.Disconnected(DateTimeOffset.UtcNow);
            }

            var state = _fsm.Process(snapshot);
            Latest = state;
            StateUpdated?.Invoke(state);
        }
    }

    private List<RegisterSample> PollOnce(IModbusReader reader)
    {
        byte unit = _config.Device.UnitId;
        var samples = new List<RegisterSample>(_config.Registers.Count);

        foreach (var block in _blocks)
        {
            if (block.Access is ModbusAccess.Coil or ModbusAccess.DiscreteInput)
            {
                bool[] bits = block.Access == ModbusAccess.Coil
                    ? reader.ReadCoils(unit, block.Start, block.Quantity)
                    : reader.ReadDiscreteInputs(unit, block.Start, block.Quantity);

                foreach (var def in block.Members)
                    samples.Add(RegisterDecoder.FromBit(def, bits[def.Address - block.Start]));
            }
            else
            {
                ushort[] words = block.Access == ModbusAccess.HoldingRegister
                    ? reader.ReadHoldingRegisters(unit, block.Start, block.Quantity)
                    : reader.ReadInputRegisters(unit, block.Start, block.Quantity);

                foreach (var def in block.Members)
                    samples.Add(RegisterDecoder.FromWords(def, words.AsSpan(def.Address - block.Start, def.Count)));
            }
        }

        return samples;
    }
}
