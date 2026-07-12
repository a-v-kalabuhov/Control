using UsrIoPoller;

// Загрузка конфигурации тегов.
var config = ConfigLoader.LoadFromFile("registers.json");

// Фабрика соединения (вызывается опросчиком при старте и переподключениях).
IModbusReader ReaderFactory() =>
    new NModbusReader(config.Device.Host, config.Device.Port, config.Device.TimeoutMs);

var poller = new Poller(config, ReaderFactory);

// --- stateful-логика подключается здесь, вне конфига ---

// Считаем цикл по фронту «открытие формы» (false→true), мёртвое время 3 с.
var cycleDetector = new EdgeCycleProcessor("MoldOpen", risingEdge: true, refractory: TimeSpan.FromSeconds(3));
cycleDetector.CycleCompleted += t =>
    Console.WriteLine($"[cycle] #{cycleDetector.Count} @ {t:HH:mm:ss.fff}");

// Стабильность подушки: замер на завершении впрыска, окно 20 циклов.
var cushion = new CushionStabilityProcessor("Injection", "Cushion", window: 20);
cushion.CushionSampled += (value, spread) =>
    Console.WriteLine($"[cushion] {value:F3} V, разброс окна: {spread:F3}");

poller.AddProcessor(cycleDetector)
      .AddProcessor(cushion);

// Печать снимка каждый опрос (для наглядности; в реальности — лог/БД/UI).
poller.SnapshotReady += snapshot =>
{
    var parts = snapshot.Tags.Values.Select(t =>
        t.Physical is { } p ? $"{t.Name}={p:F3}{t.Unit}" : $"{t.Name}={t.Value}");
    Console.WriteLine($"[{snapshot.TimestampUtc:HH:mm:ss.fff}] " + string.Join("  ", parts));
};

// Аккуратное завершение по Ctrl+C.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Опрос {config.Device.Host}:{config.Device.Port} (unit {config.Device.UnitId}) " +
                  $"каждые {config.Device.PollIntervalMs} мс. Ctrl+C для остановки.");

try
{
    await poller.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Остановлено.");
}
