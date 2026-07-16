namespace UsrConnector.Core;

/// <summary>
/// Автомат состояния ТПА. Принимает снимки сигналов по ролям (<see cref="RoleSnapshot"/>),
/// выдаёт <see cref="MachineState"/> на каждый опрос.
///
/// Ключевые решения (детально — в STATE_MACHINE.md):
/// - ЯКОРЬ ЦИКЛА — ВПРЫСК. Цикл начинается по фронту Injection ↑ (однозначный признак
///   производственного цикла; Mould closed не годится — форма смыкается и в ручных
///   операциях). Там же инкрементируется CycleCounter (счётчик износа формы).
/// - Штатное завершение — фронт EjectorFwdReached ↑ (E1).
/// - ПОДУШКА = МИНИМУМ InjectionPosition за окно цикла (от Injection ↑ до завершения).
///   Мгновенный замер не годится: точный момент спада Injection между опросами не виден,
///   а к E1 шнек уже набрал новую дозу. Минимум устойчив к дискретизации опроса.
///   Подушка публикуется при ЛЮБОМ исходе цикла — интерпретация за внешней системой.
/// - Idle и Alarm взаимоисключающие по построению: был впрыск → возможен только Alarm
///   (E1 задержался); не было впрыска → возможен только Idle (впрыск не пришёл).
/// - Таймауты адаптивные: среднее по последним N успешным auto-циклам × коэффициент;
///   до статистики — seed; сброс статистики при простое дольше порога (переналадка).
/// - Offline: N подряд неудачных опросов; текущий цикл помечается Interrupted;
///   восстановление связи → Idle.
/// </summary>
public sealed class MachineStateMachine
{
    private readonly StateMachineSettings _settings;

    // --- режим и счётчики ---
    private MachineMode _mode = MachineMode.Idle;
    private long _cycleCounter;
    private CycleCompletion _lastCompletion = CycleCompletion.NoneYet;

    // --- текущий цикл ---
    private bool _cycleActive;
    private DateTimeOffset _cycleStartUtc;
    private double _cushionMin = double.PositiveInfinity;
    private double _cushion2Min = double.PositiveInfinity;

    // --- завершённый цикл (защёлкнутые значения для публикации) ---
    private double? _lastCushion;
    private double? _lastCushion2;
    private double? _lastCycleDurationMs;
    private bool? _lastReject;

    // --- фронты ---
    private bool _prevInjection;
    private bool _prevEjectorFwd;

    // --- время/статистика ---
    private DateTimeOffset _lastActivityUtc;      // конец последнего цикла (для idle-таймаута)
    private DateTimeOffset? _lastSnapshotUtc;
    private readonly Queue<double> _cycleDurations = new();
    private double _avgCycleMs;

    // --- связь ---
    private int _failedPolls;

    public MachineStateMachine(StateMachineSettings settings, DateTimeOffset startUtc)
    {
        _settings = settings;
        _avgCycleMs = settings.SeedCycleMs;
        _lastActivityUtc = startUtc;
    }

    /// <summary>Текущее среднее успешного auto-цикла, мс (seed до накопления статистики).</summary>
    public double AverageCycleMs => _avgCycleMs;

    /// <summary>
    /// Обработать снимок опроса и вернуть актуальное состояние ТПА.
    /// Вызывается на каждый опрос (и при неудачном опросе — со снимком Disconnected).
    /// </summary>
    public MachineState Process(RoleSnapshot snapshot)
    {
        var now = snapshot.TimestampUtc;

        // ---------- 1. Связь ----------
        if (!snapshot.ConnectionOk)
        {
            _failedPolls++;
            if (_failedPolls >= _settings.OfflineAfterFailedPolls && _mode != MachineMode.Offline)
                EnterOffline();
            return BuildState(snapshot, now);
        }

        bool wasOffline = _mode == MachineMode.Offline;
        _failedPolls = 0;
        if (wasOffline)
        {
            // Восстановление связи: состояние по умолчанию, фронты пересеваем с текущего
            // снимка (не сравниваем с "до обрыва", чтобы не поймать ложный фронт).
            _mode = MachineMode.Idle;
            _lastActivityUtc = now;
            _prevInjection = snapshot.Injection ?? false;
            _prevEjectorFwd = snapshot.EjectorFwdReached ?? false;
            _lastSnapshotUtc = now;
            return BuildState(snapshot, now);
        }

        // ---------- 2. Сброс статистики при длительном простое (переналадка) ----------
        if (!_cycleActive && (now - _lastActivityUtc).TotalMilliseconds > _settings.StatisticsResetAfterMs
            && _cycleDurations.Count > 0)
        {
            _cycleDurations.Clear();
            _avgCycleMs = _settings.SeedCycleMs;
        }

        // ---------- 3. Фронты ----------
        bool injection = snapshot.Injection ?? false;
        bool ejectorFwd = snapshot.EjectorFwdReached ?? false;
        bool injectionRising = injection && !_prevInjection;
        bool ejectorFwdRising = ejectorFwd && !_prevEjectorFwd;
        _prevInjection = injection;
        _prevEjectorFwd = ejectorFwd;

        // ---------- 4. Начало цикла: фронт впрыска (якорь) ----------
        if (injectionRising)
        {
            if (_cycleActive)
            {
                // Новый впрыск при незакрытом цикле: прежний брошен нештатно.
                CloseCycle(CycleCompletion.Aborted, snapshot, now);
            }
            _cycleActive = true;
            _cycleStartUtc = now;
            _cushionMin = double.PositiveInfinity;
            _cushion2Min = double.PositiveInfinity;
            _cycleCounter++;              // износ формы: впрыск состоялся
            _mode = MachineMode.Auto;     // впрыск ⇒ производственный цикл
        }

        // ---------- 5. Накопление подушки: минимум позиции шнека за окно цикла ----------
        if (_cycleActive && snapshot.InjectionPosition is { } pos && pos < _cushionMin)
            _cushionMin = pos;
        if (_cycleActive && snapshot.InjectionPosition2 is { } pos2 && pos2 < _cushion2Min)
            _cushion2Min = pos2;

        // ---------- 6. Штатное завершение: фронт E1 ----------
        if (ejectorFwdRising && _cycleActive)
        {
            double durationMs = (now - _cycleStartUtc).TotalMilliseconds;
            UpdateAverage(durationMs);
            _lastCycleDurationMs = durationMs;
            CloseCycle(CycleCompletion.Normal, snapshot, now);
            _mode = MachineMode.Auto;     // остаёмся в auto до idle-таймаута
        }

        // ---------- 7. Таймауты (взаимоисключающие по факту впрыска) ----------
        if (_cycleActive)
        {
            // Впрыск был ⇒ возможен только alarm.
            if ((now - _cycleStartUtc).TotalMilliseconds > _settings.AlarmTimeoutMs(_avgCycleMs))
            {
                CloseCycle(CycleCompletion.Aborted, snapshot, now);
                _mode = MachineMode.Alarm;
            }
        }
        else if (_mode != MachineMode.Idle)
        {
            // Впрыска нет ⇒ возможен только idle.
            if ((now - _lastActivityUtc).TotalMilliseconds > _settings.IdleTimeoutMs(_avgCycleMs))
                _mode = MachineMode.Idle;
        }

        _lastSnapshotUtc = now;
        return BuildState(snapshot, now);
    }

    // ------------------------------------------------------------------

    private void EnterOffline()
    {
        if (_cycleActive)
            CloseCycleValuesOnly(CycleCompletion.Interrupted);
        _mode = MachineMode.Offline;
    }

    /// <summary>Закрыть цикл: защёлкнуть подушку/reject, зафиксировать исход, отметить активность.</summary>
    private void CloseCycle(CycleCompletion completion, RoleSnapshot snapshot, DateTimeOffset now)
    {
        CloseCycleValuesOnly(completion);
        _lastReject = snapshot.Reject;
        _lastActivityUtc = now;
    }

    private void CloseCycleValuesOnly(CycleCompletion completion)
    {
        _lastCompletion = completion;
        _lastCushion = double.IsPositiveInfinity(_cushionMin) ? null : _cushionMin;
        _lastCushion2 = double.IsPositiveInfinity(_cushion2Min) ? null : _cushion2Min;
        if (completion != CycleCompletion.Normal)
            _lastCycleDurationMs = null; // длительность осмысленна только для штатного цикла
        _cycleActive = false;
    }

    private void UpdateAverage(double durationMs)
    {
        _cycleDurations.Enqueue(durationMs);
        while (_cycleDurations.Count > _settings.AverageWindowCycles)
            _cycleDurations.Dequeue();
        _avgCycleMs = _cycleDurations.Average();
    }

    private MachineState BuildState(RoleSnapshot snapshot, DateTimeOffset now)
    {
        var fields = new Dictionary<string, object?>();

        // Защёлкнутые значения последнего завершённого цикла.
        if (_lastCushion is not null) fields[WellKnownFields.Cushion] = _lastCushion;
        if (_lastCushion2 is not null) fields[WellKnownFields.Cushion2] = _lastCushion2;
        if (_lastCycleDurationMs is not null) fields[WellKnownFields.LastCycleDurationMs] = _lastCycleDurationMs;
        if (_lastReject is not null) fields[WellKnownFields.Reject] = _lastReject;

        // Текущие мгновенные значения (если роли назначены и связь есть).
        if (snapshot.ConnectionOk)
        {
            if (snapshot.MoldPosition is not null) fields[WellKnownFields.MoldPosition] = snapshot.MoldPosition;
            if (snapshot.InjectionPosition is not null) fields[WellKnownFields.InjectionPosition] = snapshot.InjectionPosition;
        }

        // Дополнительные поля шаблона — прозрачно, без интерпретации.
        if (snapshot.ExtraFields is not null)
            foreach (var (k, v) in snapshot.ExtraFields)
                fields[k] = v;

        return new MachineState
        {
            Mode = _mode,
            CycleCounter = _cycleCounter,
            LastCycleCompletion = _lastCompletion,
            TimestampUtc = now,
            Fields = fields,
        };
    }
}
