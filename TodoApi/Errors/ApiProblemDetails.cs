using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TodoApi.Errors;

public static class ApiProblemDetails
{
    public static ObjectResult NotFound(
        ControllerBase controller,
        string title,
        string detail,
        string errorCode
    )
    {
        var httpContext = controller.ControllerContext.HttpContext;
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = title,
            Detail = detail,
            Type = "https://httpstatuses.com/404",
            Instance = httpContext?.Request.Path,
        };

        problemDetails.Extensions["traceId"] = httpContext?.TraceIdentifier ?? Activity.Current?.Id;
        problemDetails.Extensions["errorCode"] = errorCode;

        return new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status404NotFound,
            ContentTypes = { "application/problem+json" },
        };
    }
}
