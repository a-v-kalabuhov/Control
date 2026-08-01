namespace Wintime.Control.Core.Entities;

public class ImmCycle : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? MoldId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
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
    /// Приходит от коннектора сенсором типа <c>injectionDuration</c>.
    /// <c>null</c> — машина не отдаёт сигналы формы (штатный случай).
    /// </summary>
    public int? InjectionDurationMs { get; set; }

    /// <summary>
    /// Длительность паузы ПЕРЕД этим циклом литья, миллисекунды:
    /// предыдущее раскрытие↑ → смыкание↑. Сенсор типа <c>cyclePause</c>.
    /// Полный цикл = <see cref="InjectionDurationMs"/> + <see cref="PauseDurationMs"/>,
    /// отдельно не хранится.
    /// </summary>
    public int? PauseDurationMs { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public ShiftTask? Task { get; set; }
    public Mold? Mold { get; set; }
}
