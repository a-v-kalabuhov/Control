namespace Wintime.Control.Core.Entities;

/// <summary>
/// Эпизод «работы без задания» (PZP-04). Лёгкий конверт: число сирот-циклов,
/// эффективный конец и средняя длительность НЕ хранятся — деривятся из ImmCycles.
/// За жизнь эпизода к нему идёт ~3 записи: создание, закрытие (StartTask), назначение.
/// </summary>
public class UnplannedRun : BaseEntity
{
    public Guid ImmId { get; set; }
    public DateTime StartTime { get; set; }        // = EndTime первого сироты-цикла
    public DateTime? ClosedAt { get; set; }        // ставится в StartTask; null = открыт
    public Guid? AssignedTaskId { get; set; }      // назначенное задание (ретро-привязка)
    public string? AssignedByUserId { get; set; }  // аудит: кто назначил (User.Id)
    public DateTime? AssignedAt { get; set; }      // аудит: когда

    // Navigation
    public Imm Imm { get; set; } = null!;
    public ShiftTask? AssignedTask { get; set; }
}
