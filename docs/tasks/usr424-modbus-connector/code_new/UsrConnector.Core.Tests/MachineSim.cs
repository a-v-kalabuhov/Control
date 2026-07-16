namespace UsrConnector.Core.Tests;

/// <summary>
/// Харнесс для сценарных тестов автомата: виртуальные часы + фабрика снимков.
/// Период опроса по умолчанию 500 мс (как у реального коннектора).
/// </summary>
public sealed class MachineSim
{
    public static readonly DateTimeOffset T0 = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly MachineStateMachine _fsm;
    private DateTimeOffset _now = T0;
    private readonly TimeSpan _pollPeriod;

    public MachineSim(StateMachineSettings? settings = null, int pollPeriodMs = 500)
    {
        _pollPeriod = TimeSpan.FromMilliseconds(pollPeriodMs);
        _fsm = new MachineStateMachine(settings ?? DefaultSettings, T0);
    }

    /// <summary>Быстрые настройки для тестов: seed 10 с, alarm ×2, idle ×3, окно 3 цикла.</summary>
    public static StateMachineSettings DefaultSettings => new()
    {
        SeedCycleMs = 10_000,
        AlarmTimeoutCoef = 2.0,
        IdleTimeoutCoef = 3.0,
        AverageWindowCycles = 3,
        StatisticsResetAfterMs = 900_000,
        OfflineAfterFailedPolls = 3,
    };

    public DateTimeOffset Now => _now;
    public MachineStateMachine Fsm => _fsm;

    /// <summary>Один опрос с заданными сигналами; часы сдвигаются на период опроса.</summary>
    public MachineState Poll(bool injection = false, bool ejectorFwd = false,
        bool? reject = null, double? injPos = null, double? moldPos = null)
    {
        var state = _fsm.Process(new RoleSnapshot
        {
            TimestampUtc = _now,
            ConnectionOk = true,
            Injection = injection,
            EjectorFwdReached = ejectorFwd,
            Reject = reject,
            InjectionPosition = injPos,
            MoldPosition = moldPos,
        });
        _now += _pollPeriod;
        return state;
    }

    /// <summary>Неудачный опрос (нет связи).</summary>
    public MachineState PollDisconnected()
    {
        var state = _fsm.Process(RoleSnapshot.Disconnected(_now));
        _now += _pollPeriod;
        return state;
    }

    /// <summary>Прокрутить время тихими опросами (все сигналы неактивны).</summary>
    public MachineState Advance(TimeSpan span)
    {
        MachineState last = Poll();
        var target = _now + span - _pollPeriod;
        while (_now < target)
            last = Poll();
        return last;
    }

    /// <summary>
    /// Прогнать полный штатный цикл: впрыск (позиция шнека убывает до подушки),
    /// затем набор дозы, затем E1. Возвращает состояние после E1.
    /// </summary>
    public MachineState RunNormalCycle(double cushion = 5.0, double shot = 50.0, bool reject = false)
    {
        // Впрыск: 4 опроса, позиция падает shot -> cushion
        Poll(injection: true, injPos: shot);
        Poll(injection: true, injPos: (shot + cushion) / 2);
        Poll(injection: true, injPos: cushion);        // минимум — подушка
        Poll(injection: false, injPos: cushion + 1);   // спад впрыска, начался набор
        // Набор дозы: позиция растёт обратно
        Poll(injPos: (shot + cushion) / 2);
        Poll(injPos: shot);
        // Штатное завершение: E1 (+ вердикт качества в том же окне)
        return Poll(ejectorFwd: true, reject: reject, injPos: shot);
    }
}
