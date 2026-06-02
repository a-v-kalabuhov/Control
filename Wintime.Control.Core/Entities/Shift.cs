namespace Wintime.Control.Core.Entities;

/// <summary>
/// Рабочая смена. Задаёт временной интервал работы и перерыв внутри суток.
/// Все временны́е значения хранятся в минутах от начала суток (0 = 00:00).
/// </summary>
/// <remarks>
/// Смен может быть несколько; их интервалы не должны перекрываться.
/// Смена может переходить через полночь: если <c>StartMinutes + DurationMinutes &gt; 1440</c>,
/// она заканчивается в следующих сутках.
/// </remarks>
public class Shift : BaseEntity
{
    public int StartMinutes { get; set; }
    public int DurationMinutes { get; set; }
    public int BreakStartMinutes { get; set; }
    public int BreakDurationMinutes { get; set; }
}
