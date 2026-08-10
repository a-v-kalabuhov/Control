namespace Wintime.Control.Core.Entities;

public class ImmCycle : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? MoldId { get; set; }
    public DateTime StartTime { get; set; }

    /// <summary>
    /// <c>null</c> — цикл открыт (получен <c>currentCycle</c>, <c>lastCycle</c> с тем же
    /// номером ещё не пришёл). Открытые циклы уже учитываются в износе формы
    /// (<see cref="Cavities"/>), но не в агрегатах длительности/выпуска.
    /// </summary>
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Снапшот гнёздности ПФ (<see cref="Mold.Cavities"/>) на момент записи цикла.
    /// Mold.Cavities — изменяемое поле (гнёзда могут заглушаться при ремонте), поэтому
    /// выработку исторических циклов нельзя пересчитывать по текущему значению.
    /// Fallback для старых записей (= 0) — брать из <see cref="Mold.Cavities"/>.
    /// </summary>
    public int Cavities { get; set; }

    /// <summary>
    /// Длительность цикла литья, миллисекунды: смыкание ПФ↑ → полное раскрытие↑.
    /// Вычисляется из <see cref="InjectionStartTime"/> и <see cref="EndTime"/> по данным
    /// коннектора (контракт v2, блоки currentCycle/lastCycle).
    /// <c>null</c> — машина не отдаёт сигналы формы (штатный случай).
    /// </summary>
    public int? InjectionDurationMs { get; set; }

    /// <summary>
    /// Длительность паузы ПЕРЕД этим циклом литья, миллисекунды:
    /// предыдущее раскрытие↑ → смыкание↑.
    /// Полный цикл = <see cref="InjectionDurationMs"/> + <see cref="PauseDurationMs"/>,
    /// отдельно не хранится.
    /// </summary>
    public int? PauseDurationMs { get; set; }

    /// <summary>
    /// Момент начала впрыска (контракт v2). Источник для <see cref="InjectionDurationMs"/>.
    /// </summary>
    public DateTime? InjectionStartTime { get; set; }

    /// <summary>
    /// Подушка (минимальное значение сигнала роли InjectionPosition за время впрыска),
    /// вычисляется коннектором. <c>null</c>, пока цикл не завершён или машина не отдаёт
    /// сигнал положения инжекции.
    /// </summary>
    public decimal? Cushion { get; set; }

    /// <summary>
    /// Номер цикла как его видит коннектор (счётчик его автомата состояний). НЕ уникален
    /// сам по себе — обнуляется коннектором между сериями выпуска без разрыва связи.
    /// Идентичность цикла — пара (<see cref="CycleNumber"/>, <see cref="StartTime"/>).
    /// </summary>
    public int? CycleNumber { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public ShiftTask? Task { get; set; }
    public Mold? Mold { get; set; }
}
