using Microsoft.AspNetCore.Diagnostics;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.API.ExceptionHandling;

/// <summary>
/// Преобразует <see cref="DomainException"/> в ответ HTTP 400 с телом
/// <c>{ "message": "..." }</c> — фронтенд читает <c>error.response.data.message</c>.
/// Прочие исключения пропускает дальше по конвейеру.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domainException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(
            new { message = domainException.Message }, cancellationToken);

        return true;
    }
}
