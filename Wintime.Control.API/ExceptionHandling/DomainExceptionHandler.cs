using Microsoft.AspNetCore.Diagnostics;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.API.ExceptionHandling;

/// <summary>
/// Преобразует доменные исключения в ответы HTTP 400/403/404/409 с телом
/// <c>{ "message": "..." }</c> — фронтенд читает <c>error.response.data.message</c>.
/// Прочие исключения пропускает дальше по конвейеру.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            DomainException => StatusCodes.Status400BadRequest,
            ShiftAccessException => StatusCodes.Status403Forbidden,
            ShiftNotFoundException => StatusCodes.Status404NotFound,
            ShiftConcurrencyException => StatusCodes.Status409Conflict,
            _ => 0
        };
        if (statusCode == 0)
            return false;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new { message = exception.Message }, cancellationToken);

        return true;
    }
}
