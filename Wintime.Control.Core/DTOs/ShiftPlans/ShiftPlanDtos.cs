using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.DTOs.ShiftPlans;

public sealed class ShiftPlanDayDto
{
    public DateOnly ShiftDate { get; set; }
    public bool IsSaved { get; set; }
    public bool IsFactoryToday { get; set; }
    public List<ShiftPlanItemDto> Items { get; set; } = new();
}

public sealed class ShiftPlanItemDto
{
    public Guid? Id { get; set; }
    public int Position { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string? ShiftLeadId { get; set; }
    public string? ShiftLeadName { get; set; }
    public ShiftState State { get; set; }
    public bool CanEditLead { get; set; }
    public bool CanEditOperators { get; set; }
    public int OperatorAssignmentCount { get; set; }
}

public sealed record SetShiftLeadRequest(string? ShiftLeadId);

public sealed record StaffPersonDto(string Id, string FullName, string? EmployeeId);
