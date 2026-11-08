using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintime.Control.Core.DTOs.ShiftPlans;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/shift-staffing")]
[Authorize(Policy = "ShiftLeadOrManagerOrAdmin")]
public class ShiftStaffingController : ControllerBase
{
    private readonly IShiftStaffingService _service;

    public ShiftStaffingController(IShiftStaffingService service) => _service = service;

    [HttpGet("nearest")]
    [Authorize(Roles = Roles.ShiftLead)]
    public Task<NearestShiftDto> Nearest(CancellationToken ct) =>
        _service.GetNearestAsync(UserId, ct);

    [HttpGet("history")]
    [Authorize(Roles = Roles.ShiftLead)]
    public Task<IReadOnlyList<ShiftStaffingSummaryDto>> History(CancellationToken ct) =>
        _service.GetHistoryAsync(UserId, ct);

    [HttpGet("items/{itemId:guid}")]
    public Task<ShiftStaffingItemDto> Item(Guid itemId, CancellationToken ct) =>
        _service.GetItemAsync(itemId, Caller, ct);

    [HttpGet("items/{itemId:guid}/tasks/{taskId:guid}")]
    [Authorize(Policy = "ShiftLeadOrManagerOrAdmin")]
    public Task<ShiftStaffingTaskDto> GetTask(Guid itemId, Guid taskId, CancellationToken ct) =>
        _service.GetTaskAsync(itemId, taskId, Caller, ct);

    [HttpGet("operators")]
    public Task<IReadOnlyList<StaffPersonDto>> Operators(CancellationToken ct) =>
        _service.GetOperatorsAsync(ct);

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new ShiftAccessException("Пользователь не идентифицирован");

    private StaffingCaller Caller => new(
        UserId,
        User.IsInRole(Roles.ShiftLead),
        User.IsInRole(Roles.Manager) || User.IsInRole(Roles.Admin));
}
