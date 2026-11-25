using Microsoft.AspNetCore.Identity;
using Wintime.Control.Core.DTOs.Personnel;

namespace Wintime.Control.Core.Interfaces;

public enum PersonnelUpdateStatus
{
    NoContent,
    NotFound,
    Forbid,
    BadRequest
}

public sealed record PersonnelUpdateResult(
    PersonnelUpdateStatus Status,
    string? Message = null,
    IReadOnlyList<IdentityError>? Errors = null);

public interface IPersonnelService
{
    Task<PersonnelUpdateResult> UpdateAsync(
        string id,
        UpdatePersonnelRequestDto request,
        bool callerIsAdmin,
        CancellationToken ct);
}
