using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApi.Middleware;

namespace TodoApi.Tests.Middleware;

public class UnhandledExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenUnhandledExceptionOccurs_ReturnsProblemDetails()
    {
        var middleware = new UnhandledExceptionMiddleware(
            _ => throw new InvalidOperationException("Boom"),
            NullLogger<UnhandledExceptionMiddleware>.Instance
        );
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "trace-test-1",
        };
        context.Request.Path = "/api/test";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Contains("\"title\":\"An unexpected error occurred.\"", body);
        Assert.Contains("\"traceId\":\"trace-test-1\"", body);
        Assert.Contains("\"errorId\":", body);
        Assert.Contains("\"eventId\":1000", body);
    }
}
