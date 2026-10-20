using Wintime.Control.Core.Policies;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Core.DTOs.Planning;

/// <summary>Конфликты плана всех незавершённых заданий (PZP-07b, ADR-0022). Вычисляются на лету.</summary>
public class PlanConflictsDto
{
    public DateTime Now { get; set; }
    public int ThresholdMinutes { get; set; }
    public int PlanCount { get; set; }
    public int DeviationCount { get; set; }
    /// <summary>Уникальных заданий хотя бы в одной паре — «N заданий требуют корректировки».</summary>
    public int TaskCount { get; set; }
    /// <summary>Конфликты плана, затем отклонения; внутри — по началу пересечения.</summary>
    public List<PlanConflictDto> Items { get; set; } = new();
}

public class PlanConflictDto
{
    public ConflictKind Kind { get; set; }
    public ConflictResource Resource { get; set; }
    /// <summary>Задание с более ранним началом занятости.</summary>
    public ConflictTaskRefDto Task { get; set; } = null!;
    public ConflictTaskRefDto Other { get; set; } = null!;
    public DateTime OverlapStart { get; set; }
    public DateTime OverlapEnd { get; set; }
    public long OverlapSeconds { get; set; }
}

public class ConflictTaskRefDto
{
    public Guid Id { get; set; }
    public TaskStatus Status { get; set; }
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public Guid MoldId { get; set; }
    public string? MoldName { get; set; }
    /// <summary>Полный артикул изделия (<c>Product.FullArticle</c>).</summary>
    public string? ProductArticle { get; set; }
    public DateTime PlannedStartAt { get; set; }
}
