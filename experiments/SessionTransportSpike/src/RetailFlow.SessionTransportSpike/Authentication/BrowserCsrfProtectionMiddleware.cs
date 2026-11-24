using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RetailFlow.SessionTransportSpike.Authentication;

public sealed class BrowserCsrfProtectionMiddleware
{
    public const string CustomHeaderName = "X-RetailFlow-Request";
    public const string CustomHeaderValue = "1";
    private const string SecFetchSiteHeaderName = "Sec-Fetch-Site";

    private readonly RequestDelegate _next;
    private readonly BrowserCsrfProtectionOptions.NormalizedOrigin _trustedOrigin;
    private readonly ILogger<BrowserCsrfProtectionMiddleware> _logger;

    public BrowserCsrfProtectionMiddleware(
        RequestDelegate next,
        IOptions<BrowserCsrfProtectionOptions> options,
        ILogger<BrowserCsrfProtectionMiddleware> logger)
    {
        _next = next
            ?? throw new ArgumentNullException(nameof(next));

        ArgumentNullException.ThrowIfNull(options);

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        if (!BrowserCsrfProtectionOptions.TryParseStrictOrigin(
                options.Value.BrowserOrigin,
                out _trustedOrigin))
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(BrowserCsrfProtectionOptions),
                [
                    $"Configuration '{BrowserCsrfProtectionOptions.ConfigurationKey}' " +
                    "must be a valid http or https origin without path, query, fragment, or userinfo."
                ]);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsSafeMethod(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!HasValidCustomHeader(context.Request.Headers))
        {
            await RejectAsync(context, "missing_or_invalid_custom_header");
            return;
        }

        if (!HasValidFetchMetadata(context.Request.Headers))
        {
            await RejectAsync(context, "invalid_sec_fetch_site");
            return;
        }

        if (!HasValidOriginOrReferer(
                context.Request.Headers,
                out var failureReason))
        {
            await RejectAsync(context, failureReason);
            return;
        }

        await _next(context);
    }

    private static bool IsSafeMethod(string method)
    {
        return HttpMethods.IsGet(method) ||
               HttpMethods.IsHead(method) ||
               HttpMethods.IsOptions(method) ||
               HttpMethods.IsTrace(method);
    }

    private static bool HasValidCustomHeader(IHeaderDictionary headers)
    {
        return headers.TryGetValue(CustomHeaderName, out var values) &&
               values.Count == 1 &&
               string.Equals(
                   values[0],
                   CustomHeaderValue,
                   StringComparison.Ordinal);
    }

    private static bool HasValidFetchMetadata(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue(SecFetchSiteHeaderName, out var values) ||
            values.Count == 0)
        {
            // Browsers that do not send Fetch Metadata fall back to Origin/Referer.
            return true;
        }

        return values.Count == 1 &&
               string.Equals(
                   values[0],
                   "same-origin",
                   StringComparison.Ordinal);
    }

    private bool HasValidOriginOrReferer(
        IHeaderDictionary headers,
        out string failureReason)
    {
        if (headers.TryGetValue("Origin", out var originValues) &&
            originValues.Count > 0)
        {
            if (originValues.Count != 1)
            {
                failureReason = "multiple_origin_headers";
                return false;
            }

            var rawOrigin = originValues[0];

            if (string.Equals(
                    rawOrigin,
                    "null",
                    StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "null_origin";
                return false;
            }

            if (!BrowserCsrfProtectionOptions.TryParseStrictOrigin(
                    rawOrigin,
                    out var origin) ||
                !MatchesTrustedOrigin(origin))
            {
                failureReason = "untrusted_or_invalid_origin";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        if (!headers.TryGetValue("Referer", out var refererValues) ||
            refererValues.Count == 0)
        {
            failureReason = "missing_origin_and_referer";
            return false;
        }

        if (refererValues.Count != 1 ||
            !BrowserCsrfProtectionOptions.TryExtractRefererOrigin(
                refererValues[0],
                out var refererOrigin) ||
            !MatchesTrustedOrigin(refererOrigin))
        {
            failureReason = "untrusted_or_invalid_referer";
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private bool MatchesTrustedOrigin(
        BrowserCsrfProtectionOptions.NormalizedOrigin candidate)
    {
        return string.Equals(
                   candidate.Scheme,
                   _trustedOrigin.Scheme,
                   StringComparison.Ordinal) &&
               string.Equals(
                   candidate.Host,
                   _trustedOrigin.Host,
                   StringComparison.Ordinal) &&
               candidate.Port == _trustedOrigin.Port;
    }

    private async Task RejectAsync(HttpContext context, string reason)
    {
        _logger.LogWarning(
            "CSRF protection rejected {Method} {Path} ({Reason}).",
            context.Request.Method,
            context.Request.Path.Value,
            reason);

        context.Response.StatusCode =
            StatusCodes.Status403Forbidden;

        context.Response.Headers["Cache-Control"] =
            "no-store";

        await Results.Problem(
            title: "CSRF validation failed.",
            detail: "The request did not satisfy browser CSRF protection requirements.",
            statusCode: StatusCodes.Status403Forbidden)
            .ExecuteAsync(context);
    }
}
