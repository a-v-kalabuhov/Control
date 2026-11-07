using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.DTOs.Tasks;

namespace Wintime.Control.Core.DTOs.Dashboard;

/// <summary>Данные карточки ТПА литейщика за серверно определённую текущую смену.</summary>
public class ImmCardShiftDto
{
    public DateTime ShiftStartAt { get; set; }
    public DateTime ShiftEndAt { get; set; }
    public List<EffectiveStatusSegmentDto> StatusSegments { get; set; } = new();
    public List<TaskDto> ActiveTasks { get; set; } = new();
    public List<TaskDto> PlannedTasks { get; set; } = new();
}
