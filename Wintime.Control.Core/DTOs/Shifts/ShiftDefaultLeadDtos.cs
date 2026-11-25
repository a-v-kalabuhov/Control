namespace Wintime.Control.Core.DTOs.Shifts;

public sealed class ShiftDefaultLeadSettingsDto
{
    public IReadOnlyList<ShiftDefaultLeadItemDto> Items { get; set; } = [];
    public ShiftFallbackManagerDto? FallbackManager { get; set; }
}

public sealed class ShiftDefaultLeadItemDto
{
    public int Position { get; set; }
    public int Number { get; set; }
    public int StartMinutes { get; set; }
    public int DurationMinutes { get; set; }
    public int BreakStartMinutes { get; set; }
    public int BreakDurationMinutes { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string? DefaultShiftLeadId { get; set; }
    public string? DefaultShiftLeadName { get; set; }
    public string? DefaultShiftLeadEmployeeId { get; set; }
}

public sealed class ShiftFallbackManagerDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SaveShiftDefaultLeadsRequestDto
{
    public List<SaveShiftDefaultLeadItemDto> Items { get; set; } = [];
}

public sealed class SaveShiftDefaultLeadItemDto
{
    public int Position { get; set; }
    public string? ShiftLeadId { get; set; }
}
