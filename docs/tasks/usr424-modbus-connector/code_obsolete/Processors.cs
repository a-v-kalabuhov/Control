namespace UsrIoPoller;

/// <summary>
/// Пример stateful-процессора: детектор завершённого цикла по фронту булева тега
/// с «мёртвым временем» (refractory), чтобы схлопнуть дребезг/хвост одного события.
/// Считает ФАКТ цикла, а не число импульсов.
/// </summary>
public sealed class EdgeCycleProcessor : ITagProcessor
{
    private readonly string _tagName;
    private readonly bool _risingEdge;      // true: срабатывание на false→true
    private readonly TimeSpan _refractory;

    private bool? _prev;
    private DateTime _lastCycleUtc = DateTime.MinValue;

    /// <summary>Число засчитанных циклов.</summary>
    public long Count { get; private set; }

    /// <summary>Событие на каждый засчитанный цикл (передаётся метка времени).</summary>
    public event Action<DateTime>? CycleCompleted;

    public EdgeCycleProcessor(string tagName, bool risingEdge, TimeSpan refractory)
    {
        _tagName = tagName;
        _risingEdge = risingEdge;
        _refractory = refractory;
    }

    public void Process(TagSnapshot snapshot)
    {
        if (!snapshot.Tags.TryGetValue(_tagName, out var sample))
            return;

        // Ожидаем булев тег; если замапплен иначе — трактуем raw как 0/1.
        bool state = sample.Value is bool b ? b : sample.RawNumeric != 0;

        if (_prev is null)
        {
            _prev = state;
            return; // первый снимок — только фиксируем базу
        }

        bool edge = _risingEdge ? (!_prev.Value && state) : (_prev.Value && !state);
        _prev = state;

        if (!edge)
            return;

        if (snapshot.TimestampUtc - _lastCycleUtc < _refractory)
            return; // хвост того же события — игнорируем

        _lastCycleUtc = snapshot.TimestampUtc;
        Count++;
        CycleCompleted?.Invoke(snapshot.TimestampUtc);
    }
}

/// <summary>
/// Пример stateful-процессора: контроль стабильности «подушки» (AI2).
/// Замер привязывается к спаду фазы впрыска (true→false заданного тега),
/// после чего отслеживается скользящий разброс значения подушки.
/// </summary>
public sealed class CushionStabilityProcessor : ITagProcessor
{
    private readonly string _injectionTag; // булев тег фазы впрыска
    private readonly string _cushionTag;   // аналоговый тег подушки
    private readonly int _window;
    private readonly Queue<double> _samples = new();

    private bool? _prevInjection;

    /// <summary>Последний зафиксированный размах подушки (max−min) в окне.</summary>
    public double? SpreadInWindow { get; private set; }

    /// <summary>Событие: зафиксирован замер подушки на завершении впрыска.</summary>
    public event Action<double, double?>? CushionSampled; // (value, spread)

    public CushionStabilityProcessor(string injectionTag, string cushionTag, int window = 20)
    {
        _injectionTag = injectionTag;
        _cushionTag = cushionTag;
        _window = Math.Max(2, window);
    }

    public void Process(TagSnapshot snapshot)
    {
        if (!snapshot.Tags.TryGetValue(_injectionTag, out var inj))
            return;

        bool injecting = inj.Value is bool b ? b : inj.RawNumeric != 0;

        if (_prevInjection is true && !injecting) // спад: впрыск завершился
        {
            if (snapshot.Tags.TryGetValue(_cushionTag, out var cush) && cush.Physical is { } value)
            {
                _samples.Enqueue(value);
                while (_samples.Count > _window) _samples.Dequeue();

                SpreadInWindow = _samples.Count >= 2 ? _samples.Max() - _samples.Min() : null;
                CushionSampled?.Invoke(value, SpreadInWindow);
            }
        }

        _prevInjection = injecting;
    }
}
