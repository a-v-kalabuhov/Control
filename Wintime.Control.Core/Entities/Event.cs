using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Запись в журнале событий.
/// </summary>
public class Event : BaseEntity
{
    public Guid ImmId { get; set; }
    public EventType EventType { get; set; }
    public Guid? ReasonId { get; set; } // Ссылка на DowntimeReason
    public string? ReasonName { get; set; } // Денормализация для отчётов
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds => EndTime.HasValue ? (int)(EndTime.Value - StartTime).TotalSeconds : 0;
    public string? PersonnelId { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public DowntimeReason? Reason { get; set; }
    public User? Personnel { get; set; }
}