using Wintime.Control.Core.DTOs.ShiftClosures;

namespace Wintime.Control.Core.Exceptions;

public sealed class ShiftClosureValidationException : DomainException
{
    public ShiftClosureValidationException(
        string code,
        string message,
        IReadOnlyList<ShiftClosureFieldError> errors) : base(message)
    {
        Code = code;
        Errors = errors;
    }

    public string Code { get; }
    public IReadOnlyList<ShiftClosureFieldError> Errors { get; }
}
