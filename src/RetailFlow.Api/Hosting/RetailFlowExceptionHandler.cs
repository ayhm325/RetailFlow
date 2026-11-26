using System.Diagnostics;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Orders;

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

        if (exception is OrderStateConflictException)
        {
            return await WriteProblemDetailsAsync(
                httpContext,
                exception,
                StatusCodes.Status409Conflict,
                "Order State Conflict",
                "The requested operation is not valid for the current order state. Refresh the order and try again.",
                "order_state_conflict",
                traceId,
                isWarning: true);
        }

        if (exception is OrderConcurrencyConflictException)
        {
            return await WriteProblemDetailsAsync(
                httpContext,
                exception,
                StatusCodes.Status409Conflict,
                "Order Concurrency Conflict",
                "The order or its inventory changed while the operation was in progress. Refresh the order and retry if appropriate.",
                "order_concurrency_conflict",
                traceId,
                isWarning: true);
        }

        var isTransientDb = IsTransientDatabaseFailure(exception);
        var status = isTransientDb ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
        var code = isTransientDb ? "service_unavailable" : "internal_server_error";

        return await WriteProblemDetailsAsync(
            httpContext,
            exception,
            status,
            isTransientDb ? "Service Temporarily Unavailable" : "An unexpected error occurred",
            isTransientDb
                ? "A required dependency is temporarily unavailable. Please retry shortly."
                : "An unexpected internal error occurred while processing the request.",
            code,
            traceId,
            isWarning: false);
    }

    private async ValueTask<bool> WriteProblemDetailsAsync(
        HttpContext httpContext,
        Exception exception,
        int status,
        string title,
        string detail,
        string code,
        string traceId,
        bool isWarning)
    {
        if (isWarning)
        {
            _logger.LogWarning(
                exception,
                "Handled conflict exception mapped to HTTP {StatusCode} Code={ErrorCode} TraceId={TraceId} Path={Path}.",
                status,
                code,
                traceId,
                httpContext.Request.Path);
        }
        else
        {
            _logger.LogError(
                exception,
                "Unhandled exception mapped to HTTP {StatusCode} Code={ErrorCode} TraceId={TraceId} Path={Path}.",
                status,
                code,
                traceId,
                httpContext.Request.Path);
        }

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
                    Type = "about:blank",
                    Status = status,
                    Title = title,
                    Detail = detail,
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
