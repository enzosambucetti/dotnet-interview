using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using TodoApi.Errors;

namespace TodoApi.Middleware;

public class UnhandledExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UnhandledExceptionMiddleware> _logger;

    public UnhandledExceptionMiddleware(
        RequestDelegate next,
        ILogger<UnhandledExceptionMiddleware> logger
    )
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var errorId = Guid.NewGuid().ToString("N");

            _logger.LogError(
                ApiErrorEventIds.UnhandledHttpException,
                exception,
                "Unhandled HTTP exception. ErrorId: {ErrorId}; TraceId: {TraceId}; Path: {Path}",
                errorId,
                context.TraceIdentifier,
                context.Request.Path
            );

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "Use errorId and traceId to locate the server log entry.",
                Type = "https://httpstatuses.com/500",
                Instance = context.Request.Path,
            };

            problemDetails.Extensions["traceId"] = context.TraceIdentifier;
            problemDetails.Extensions["errorId"] = errorId;
            problemDetails.Extensions["eventId"] = ApiErrorEventIds.UnhandledHttpException.Id;

            await JsonSerializer.SerializeAsync(context.Response.Body, problemDetails);
        }
    }
}
