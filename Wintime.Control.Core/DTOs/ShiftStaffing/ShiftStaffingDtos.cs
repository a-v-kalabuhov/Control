using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Tasks;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.DTOs.ShiftStaffing;

public sealed record NearestShiftDto(ShiftStaffingSummaryDto? Item);
public sealed record StaffingCaller(string UserId, bool IsShiftLead, bool IsManagerOrAdmin);

public class ShiftStaffingSummaryDto
{
    public Guid ItemId { get; set; }
    public DateOnly ShiftDate { get; set; }
    public int Position { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public ShiftState State { get; set; }
    public bool CanEditOperators { get; set; }
}

public class ShiftStaffingItemDto
{
    public ShiftStaffingSummaryDto Shift { get; set; } = new();
    public string ShiftLeadId { get; set; } = string.Empty;
    public string ShiftLeadName { get; set; } = string.Empty;
    public List<ShiftStaffingImmDto> Imms { get; set; } = new();
}

public class ShiftStaffingImmDto
{
    public Guid ImmId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? InventoryNumber { get; set; }
    public bool IsActive { get; set; }
    public string EffectiveStatus { get; set; } = Wintime.Control.Core.Constants.EffectiveStatus.Offline;
    public List<TaskDto> Tasks { get; set; } = new();
    public List<ShiftOperatorAssignmentDto> Assignments { get; set; } = new();
    public int ActiveOperatorCount { get; set; }
}

public class ShiftOperatorAssignmentDto
{
    public Guid Id { get; set; }
    public Guid ImmId { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public string? OperatorEmployeeId { get; set; }
    public int Slot { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
    public DateTime WorkStart { get; set; }
    public DateTime WorkEnd { get; set; }
    public bool IsWorkIntervalEmpty { get; set; }
}
