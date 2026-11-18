using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Wintime.Control.API.ExceptionHandling;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.Exceptions;
using Xunit;

namespace Wintime.Control.Tests.Integration.Errors;

public class DomainExceptionHandlerTests
{
    [Fact]
    public async Task Domain_exception_retains_message_only_response_contract()
    {
        var context = CreateContext();

        var handled = await new DomainExceptionHandler().TryHandleAsync(
            context, new DomainException("Нарушение доменного правила"), CancellationToken.None);
        using var body = await ReadBodyAsync(context);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("message");
        body.RootElement.GetProperty("message").GetString().Should().Be("Нарушение доменного правила");
    }

    [Fact]
    public async Task Shift_closure_validation_exception_returns_structured_field_errors()
    {
        var context = CreateContext();
        var taskId = Guid.NewGuid();
        var exception = new ShiftClosureValidationException(
            "shiftClosureValidationFailed",
            "Проверьте показатели закрытия смены",
            new[] { new ShiftClosureFieldError(taskId, "manualGoodQuantity", "required") });

        var handled = await new DomainExceptionHandler().TryHandleAsync(
            context, exception, CancellationToken.None);
        using var body = await ReadBodyAsync(context);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
        body.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().Equal("message", "code", "errors");
        body.RootElement.GetProperty("message").GetString()
            .Should().Be("Проверьте показатели закрытия смены");
        body.RootElement.GetProperty("code").GetString().Should().Be("shiftClosureValidationFailed");
        var error = body.RootElement.GetProperty("errors").EnumerateArray().Single();
        error.GetProperty("taskId").GetGuid().Should().Be(taskId);
        error.GetProperty("field").GetString().Should().Be("manualGoodQuantity");
        error.GetProperty("code").GetString().Should().Be("required");
    }

    private static DefaultHttpContext CreateContext() => new()
    {
        Response = { Body = new MemoryStream() }
    };

    private static async Task<JsonDocument> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
