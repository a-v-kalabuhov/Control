using UsrConnector.Core;

// ============================================================================
// Хост коннектора — ВЕРХНИЙ слой. Здесь (и только здесь) появится внешний
// транспорт: шаблон устройства, упаковка сообщений, MQTT / REST.
// Пока — консольный вывод MachineState для отладки на живом устройстве.
//
// Контракт с ядром: подписка на ConnectorEngine.StateUpdated.
// В режиме Offline сообщения наружу НЕ отправляются (по договорённости
// верхний слой сам понимает это по MachineState.Mode).
// ============================================================================

var configPath = args.Length > 0 ? args[0] : "connector.json";
var config = ConfigLoader.LoadFromFile(configPath);

var engine = new ConnectorEngine(
    config,
    () => new NModbusReader(config.Device.Host, config.Device.Port, config.Device.TimeoutMs));

MachineMode? lastPrintedMode = null;
long lastPrintedCycle = -1;

engine.StateUpdated += state =>
{
    // Печатаем при смене режима или номера цикла, чтобы не заливать консоль.
    bool changed = state.Mode != lastPrintedMode || state.CycleCounter != lastPrintedCycle;
    if (!changed) return;
    lastPrintedMode = state.Mode;
    lastPrintedCycle = state.CycleCounter;

    var fields = string.Join(", ", state.Fields.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));
    Console.WriteLine(
        $"[{state.TimestampUtc:HH:mm:ss.fff}] mode={state.Mode} cycle={state.CycleCounter} " +
        $"completion={state.LastCycleCompletion}" + (fields.Length > 0 ? $" | {fields}" : ""));
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Коннектор: {config.Device.Host}:{config.Device.Port} (unit {config.Device.UnitId}), " +
                  $"профиль {config.Profile}, опрос {config.Device.PollIntervalMs} мс. Ctrl+C — стоп.");

try
{
    await engine.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Остановлено.");
}

static string FormatValue(object? v) => v switch
{
    double d => d.ToString("F3"),
    null => "null",
    _ => v.ToString() ?? "null",
};
