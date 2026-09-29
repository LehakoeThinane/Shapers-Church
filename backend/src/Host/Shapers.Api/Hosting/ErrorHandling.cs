using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Shapers.Api.Hosting;

/// <summary>
/// Turns expected failures into problem responses. Unexpected exceptions become a plain 500 with a
/// trace id, never a stack trace.
/// </summary>
internal sealed partial class ShapersExceptionHandler(IProblemDetailsService problems, ILogger<ShapersExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            DomainRuleException rule => (StatusCodes.Status400BadRequest, rule.Message, rule.Code),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Someone else changed this record. Reload and try again.", "concurrency_conflict"),
            BadHttpRequestException bad => (bad.StatusCode, "The request was not valid.", "bad_request"),
            _ => (StatusCodes.Status500InternalServerError, "Something went wrong on our side.", "server_error"),
        };

        if (status >= 500)
        {
            LogUnhandled(logger, exception);
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code, ["traceId"] = httpContext.TraceIdentifier },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
