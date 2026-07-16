using Xunit;

namespace UsrConnector.Core.Tests;

/// <summary>
/// Исполняемая спецификация автомата состояния ТПА.
/// Каждый тест соответствует согласованному правилу из STATE_MACHINE.md.
/// </summary>
public class MachineStateMachineTests
{
    // ---------------------------------------------------------------
    // Старт и базовые режимы
    // ---------------------------------------------------------------

    [Fact]
    public void StartsInIdle_WithZeroCounter_AndNoCycles()
    {
        var sim = new MachineSim();
        var s = sim.Poll();

        Assert.Equal(MachineMode.Idle, s.Mode);
        Assert.Equal(0, s.CycleCounter);
        Assert.Equal(CycleCompletion.NoneYet, s.LastCycleCompletion);
    }

    [Fact]
    public void InjectionRisingEdge_SwitchesToAuto_AndIncrementsCounter()
    {
        var sim = new MachineSim();
        sim.Poll();

        var s = sim.Poll(injection: true);

        Assert.Equal(MachineMode.Auto, s.Mode);
        Assert.Equal(1, s.CycleCounter); // якорь цикла — впрыск; инкремент на фронте
    }

    [Fact]
    public void CounterIncrementsOnRisingEdgeOnly_NotWhileInjectionHeld()
    {
        var sim = new MachineSim();
        sim.Poll(injection: true);
        sim.Poll(injection: true);
        var s = sim.Poll(injection: true);

        Assert.Equal(1, s.CycleCounter); // удержание сигнала — не новые циклы
    }

    // ---------------------------------------------------------------
    // Штатный цикл: E1, подушка, длительность, reject
    // ---------------------------------------------------------------

    [Fact]
    public void NormalCycle_CompletesOnEjectorFwd_WithNormalCompletion()
    {
        var sim = new MachineSim();
        var s = sim.RunNormalCycle();

        Assert.Equal(CycleCompletion.Normal, s.LastCycleCompletion);
        Assert.Equal(1, s.CycleCounter);
        Assert.Equal(MachineMode.Auto, s.Mode); // auto держится до idle-таймаута
    }

    [Fact]
    public void Cushion_IsMinimumOfInjectionPosition_OverCycleWindow()
    {
        // Подушка — минимум за окно цикла, НЕ мгновенное значение на E1
        // (к E1 шнек уже набрал новую дозу: мгновенный замер дал бы 50, а не 5).
        var sim = new MachineSim();
        var s = sim.RunNormalCycle(cushion: 5.0, shot: 50.0);

        Assert.Equal(5.0, Assert.IsType<double>(s.Fields[WellKnownFields.Cushion]));
    }

    [Fact]
    public void CycleDuration_PublishedForNormalCycle()
    {
        var sim = new MachineSim();
        var s = sim.RunNormalCycle();

        var ms = Assert.IsType<double>(s.Fields[WellKnownFields.LastCycleDurationMs]);
        Assert.InRange(ms, 1, 60_000);
    }

    [Fact]
    public void Reject_LatchedFromCompletionWindow()
    {
        var sim = new MachineSim();
        var s = sim.RunNormalCycle(reject: true);

        Assert.True(Assert.IsType<bool>(s.Fields[WellKnownFields.Reject]));
    }

    // ---------------------------------------------------------------
    // Alarm: впрыск был, E1 не наступил
    // ---------------------------------------------------------------

    [Fact]
    public void HungCycle_BecomesAlarm_AfterAlarmTimeout()
    {
        var sim = new MachineSim(); // seed 10 с, alarm ×2 => 20 с
        sim.Poll(injection: true, injPos: 50);
        sim.Poll(injection: false, injPos: 5); // впрыск закончился, но E1 не приходит

        var s = sim.Advance(TimeSpan.FromSeconds(25));

        Assert.Equal(MachineMode.Alarm, s.Mode);
        Assert.Equal(CycleCompletion.Aborted, s.LastCycleCompletion);
    }

    [Fact]
    public void AbortedCycle_StillCountsWear_AndPublishesCushion()
    {
        // Принципы: износ считается по впрыску (независимо от исхода);
        // подушка публикуется при любом исходе — интерпретация за внешней системой.
        var sim = new MachineSim();
        sim.Poll(injection: true, injPos: 50);
        sim.Poll(injection: true, injPos: 7); // минимум до обрыва
        var s = sim.Advance(TimeSpan.FromSeconds(25));

        Assert.Equal(1, s.CycleCounter);
        Assert.Equal(7.0, Assert.IsType<double>(s.Fields[WellKnownFields.Cushion]));
        Assert.False(s.Fields.ContainsKey(WellKnownFields.LastCycleDurationMs)); // длительность — только Normal
    }

    [Fact]
    public void AlarmClears_OnNextNormalCycle()
    {
        var sim = new MachineSim();
        sim.Poll(injection: true);
        sim.Advance(TimeSpan.FromSeconds(25)); // -> alarm

        var s = sim.RunNormalCycle();

        Assert.Equal(MachineMode.Auto, s.Mode);
        Assert.Equal(CycleCompletion.Normal, s.LastCycleCompletion);
        Assert.Equal(2, s.CycleCounter);
    }

    [Fact]
    public void NewInjectionWhileCycleActive_AbortsPrevious_AndStartsNew()
    {
        // ТПА пропустил E1 и начал новый цикл: прежний закрывается как Aborted.
        var sim = new MachineSim();
        sim.Poll(injection: true, injPos: 50);
        sim.Poll(injection: false, injPos: 5);

        var s = sim.Poll(injection: true, injPos: 50); // новый впрыск без E1

        Assert.Equal(2, s.CycleCounter);
        Assert.Equal(CycleCompletion.Aborted, s.LastCycleCompletion);
        Assert.Equal(MachineMode.Auto, s.Mode);
    }

    // ---------------------------------------------------------------
    // Idle: впрыска не было
    // ---------------------------------------------------------------

    [Fact]
    public void NoInjection_LongSilence_BecomesIdle_NotAlarm()
    {
        // Idle и Alarm взаимоисключающие: без впрыска возможен только Idle.
        var sim = new MachineSim(); // seed 10 с, idle ×3 => 30 с
        sim.RunNormalCycle();       // auto

        var s = sim.Advance(TimeSpan.FromSeconds(120));

        Assert.Equal(MachineMode.Idle, s.Mode);
        Assert.NotEqual(CycleCompletion.Aborted, s.LastCycleCompletion);
    }

    [Fact]
    public void MouldClosedWithoutInjection_IsNotACycle()
    {
        // Смыкание формы без впрыска (ручная операция) циклом не считается.
        var sim = new MachineSim();
        var fsm = sim.Fsm;

        var s = fsm.Process(new RoleSnapshot
        {
            TimestampUtc = MachineSim.T0,
            ConnectionOk = true,
            MouldClosed = true,
        });

        Assert.Equal(0, s.CycleCounter);
        Assert.Equal(MachineMode.Idle, s.Mode);
    }

    // ---------------------------------------------------------------
    // Offline и Interrupted
    // ---------------------------------------------------------------

    [Fact]
    public void ConnectionLoss_AfterThreshold_BecomesOffline()
    {
        var sim = new MachineSim(); // порог: 3 неудачных опроса
        sim.Poll();
        sim.PollDisconnected();
        sim.PollDisconnected();
        var s = sim.PollDisconnected();

        Assert.Equal(MachineMode.Offline, s.Mode);
    }

    [Fact]
    public void SingleFailedPoll_DoesNotTriggerOffline()
    {
        // Демпфер против ложных Interrupted на единичной сетевой ошибке.
        var sim = new MachineSim();
        sim.Poll(injection: true);
        var s = sim.PollDisconnected();

        Assert.NotEqual(MachineMode.Offline, s.Mode);
    }

    [Fact]
    public void ConnectionLossDuringCycle_MarksInterrupted()
    {
        var sim = new MachineSim();
        sim.Poll(injection: true, injPos: 50);
        sim.PollDisconnected();
        sim.PollDisconnected();
        var s = sim.PollDisconnected();

        Assert.Equal(MachineMode.Offline, s.Mode);
        Assert.Equal(CycleCompletion.Interrupted, s.LastCycleCompletion);
        Assert.Equal(1, s.CycleCounter); // износ уже посчитан на фронте впрыска
    }

    [Fact]
    public void Reconnect_GoesToIdle_AndDoesNotCatchFalseEdges()
    {
        var sim = new MachineSim();
        sim.Poll(injection: true);
        sim.PollDisconnected();
        sim.PollDisconnected();
        sim.PollDisconnected(); // -> offline

        // Восстановление: Injection всё ещё активен на контактах — это НЕ новый фронт.
        var s = sim.Poll(injection: true);

        Assert.Equal(MachineMode.Idle, s.Mode);
        Assert.Equal(1, s.CycleCounter); // ложного инкремента нет
    }

    // ---------------------------------------------------------------
    // Адаптивные таймауты и статистика
    // ---------------------------------------------------------------

    [Fact]
    public void AverageCycle_AdaptsToObservedNormalCycles()
    {
        var sim = new MachineSim(); // seed 10 000 мс
        sim.RunNormalCycle();       // реальный цикл ~3.5 с (7 опросов × 500 мс)

        Assert.True(sim.Fsm.AverageCycleMs < MachineSim.DefaultSettings.SeedCycleMs);
    }

    [Fact]
    public void AlarmTimeout_UsesAdaptedAverage_NotSeed()
    {
        var sim = new MachineSim();
        sim.RunNormalCycle(); // среднее ~3.5 с => alarm ~7 с (вместо seed-овских 20 с)

        sim.Poll(injection: true, injPos: 50);
        sim.Poll(injection: false, injPos: 5);
        var s = sim.Advance(TimeSpan.FromSeconds(10)); // > 7 с, но < 20 с

        Assert.Equal(MachineMode.Alarm, s.Mode);
    }

    [Fact]
    public void Statistics_ResetAfterLongStop()
    {
        var settings = MachineSim.DefaultSettings with { StatisticsResetAfterMs = 60_000 };
        var sim = new MachineSim(settings);
        sim.RunNormalCycle();
        Assert.True(sim.Fsm.AverageCycleMs < settings.SeedCycleMs);

        sim.Advance(TimeSpan.FromSeconds(90)); // дольше порога: вероятная переналадка

        Assert.Equal(settings.SeedCycleMs, sim.Fsm.AverageCycleMs);
    }

    // ---------------------------------------------------------------
    // Контракт MachineState
    // ---------------------------------------------------------------

    [Fact]
    public void CurrentPositions_PublishedAsFields_WhenRolesAssigned()
    {
        var sim = new MachineSim();
        var s = sim.Poll(injPos: 42.0, moldPos: 100.0);

        Assert.Equal(42.0, s.Fields[WellKnownFields.InjectionPosition]);
        Assert.Equal(100.0, s.Fields[WellKnownFields.MoldPosition]);
    }

    [Fact]
    public void ExtraTemplateFields_PassedThroughTransparently()
    {
        // Поля шаблона (например, чиллер) пробрасываются без интерпретации.
        var sim = new MachineSim();
        var s = sim.Fsm.Process(new RoleSnapshot
        {
            TimestampUtc = MachineSim.T0,
            ConnectionOk = true,
            ExtraFields = new Dictionary<string, object?> { ["chillerTemp"] = 12.5 },
        });

        Assert.Equal(12.5, s.Fields["chillerTemp"]);
    }
}
