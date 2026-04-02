using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Entities;

public class Task : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid MoldId { get; set; }
    public string? PersonnelId { get; set; } // Ссылка на User.Id
    public int PlanQuantity { get; set; }
    public int ActualQuantity { get; set; }
    public Wintime.Control.Core.Enums.TaskStatus Status { get; set; } = Wintime.Control.Core.Enums.TaskStatus.Draft;
    public string? Note { get; set; }
    public string? CloseReason { get; set; }

    public DateTime? IssuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public Mold Mold { get; set; } = null!;
    public User? Personnel { get; set; }
}