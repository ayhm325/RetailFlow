using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Centralized RFC 9457 ProblemDetails exception handler.
/// Translates unexpected infrastructure/application faults into sanitized 500/503 responses
/// without exposing SQL details, stack traces, or internal paths.
/// </summary>
public sealed class RetailFlowExceptionHandler : IExceptionHandler
{
    private readonly ILogger<RetailFlowExceptionHandler> _logger;

    public RetailFlowExceptionHandler(ILogger<RetailFlowExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation("Request was cancelled by client for Path={Path}.", httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        var isTransientDb = IsTransientDatabaseFailure(exception);
        var status = isTransientDb ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
        var code = isTransientDb ? "service_unavailable" : "internal_server_error";

        _logger.LogError(
            exception,
            "Unhandled exception mapped to HTTP {StatusCode} Code={ErrorCode} TraceId={TraceId} Path={Path}.",
            status,
            code,
            traceId,
            httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = status,
                    Title = isTransientDb ? "Service Temporarily Unavailable" : "An unexpected error occurred",
                    Detail = isTransientDb
                        ? "A required dependency is temporarily unavailable. Please retry shortly."
                        : "An unexpected internal error occurred while processing the request.",
                    Instance = httpContext.Request.Path,
                    Extensions =
                    {
                        ["code"] = code,
                        ["traceId"] = traceId
                    }
                }
            });
        }

        return false;
    }

    private static bool IsTransientDatabaseFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }

            if (current is SqlException sqlEx &&
                sqlEx.Number is -2 or 53 or 4060 or 10928 or 10929 or 40197 or 40501 or 40613)
            {
                return true;
            }
        }

        return false;
    }
}
