using Microsoft.AspNetCore.Diagnostics;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.API.ExceptionHandling;

/// <summary>
/// Преобразует доменные исключения в ответы HTTP 400/403/404/409 с телом
/// <c>{ "message": "..." }</c> — фронтенд читает <c>error.response.data.message</c>.
/// Ошибки закрытия смены дополнительно содержат <c>code</c> и <c>errors</c>.
/// Прочие исключения пропускает дальше по конвейеру.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            ShiftClosureValidationException => StatusCodes.Status400BadRequest,
            TelemetryCleanupTimeoutException => StatusCodes.Status504GatewayTimeout,
            DomainException => StatusCodes.Status400BadRequest,
            ShiftAccessException => StatusCodes.Status403Forbidden,
            ShiftNotFoundException => StatusCodes.Status404NotFound,
            ShiftConcurrencyException => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (statusCode == 0)
            return false;

        httpContext.Response.StatusCode = statusCode;
        object response = exception switch
        {
            ShiftClosureValidationException validation => new
            {
                message = validation.Message,
                code = validation.Code,
                errors = validation.Errors
            },
            _ => new { message = exception.Message }
        };
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }
}
