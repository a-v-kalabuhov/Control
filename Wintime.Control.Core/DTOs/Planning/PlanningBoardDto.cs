using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.DTOs.Planning;

/// <summary>План-график цеха (PZP-07a): строки ТПА с заданиями, пересекающими окно <c>[From, To)</c>.</summary>
public class PlanningBoardDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    /// <summary>Момент расчёта (UTC) — от него фронт рисует линию «сейчас» и факт «до сейчас».</summary>
    public DateTime Now { get; set; }
    public List<PlanningBoardRowDto> Rows { get; set; } = new();
}

public class PlanningBoardRowDto
{
    public BoardImmDto Imm { get; set; } = null!;
    /// <summary>По плановому началу, затем по Id.</summary>
    public List<BoardTaskDto> Tasks { get; set; } = new();
}

public class BoardImmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class BoardTaskDto
{
    public TaskDto Task { get; set; } = null!;
    /// <summary>Полный артикул изделия задания (<c>Product.FullArticle</c>, ADR-0020) для подписи бара.</summary>
    public string? ProductArticle { get; set; }
    public BoardPlan Plan { get; set; } = null!;
    public BoardFact Fact { get; set; } = null!;
    public long? BufferRemainingSeconds { get; set; }
    public long? OverdueNotStartedSeconds { get; set; }
    public long? SetupLagSeconds { get; set; }
    /// <summary>Конфликты плана с участием задания (PZP-07b): задание — <c>Task</c> или <c>Other</c> пары.</summary>
    public List<PlanConflictDto> Conflicts { get; set; } = new();
}
