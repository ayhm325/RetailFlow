using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace RetailFlow.SessionTransportSpike.Authentication;

public sealed class BrowserCsrfProtectionOptions
{
    public const string SectionName = "Security:BrowserCsrf";
    public const string ConfigurationKey = "Security:BrowserCsrf:BrowserOrigin";

    public const string RequestHeaderName = "X-RetailFlow-Request";
    public const string RequestHeaderValue = "1";

    [Required(AllowEmptyStrings = false)]
    public string BrowserOrigin { get; set; } = string.Empty;

    public static bool IsValidBrowserOrigin(string? value)
    {
        return TryParseStrictOrigin(value, out _);
    }

    internal static bool TryParseStrictOrigin(
        string? rawOrigin,
        out NormalizedOrigin origin)
    {
        origin = default;

        if (string.IsNullOrWhiteSpace(rawOrigin))
        {
            return false;
        }

        if (!Uri.TryCreate(rawOrigin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

        // HTTP is allowed only for local loopback development.
        var isLocalDevelopment =
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback;

        if (!isHttps && !isLocalDevelopment)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var canonicalOrigin = uri.GetLeftPart(UriPartial.Authority);

        if (!string.Equals(rawOrigin, canonicalOrigin, StringComparison.Ordinal))
        {
            return false;
        }

        origin = new NormalizedOrigin(
            uri.Scheme.ToLowerInvariant(),
            uri.Host.ToLowerInvariant(),
            uri.Port);

        return true;
    }

    internal static bool TryExtractRefererOrigin(
        string? rawReferer,
        out NormalizedOrigin origin)
    {
        origin = default;

        if (string.IsNullOrWhiteSpace(rawReferer))
        {
            return false;
        }

        if (!Uri.TryCreate(rawReferer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

        var isLocalDevelopment =
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback;

        if ((!isHttps && !isLocalDevelopment) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        origin = new NormalizedOrigin(
            uri.Scheme.ToLowerInvariant(),
            uri.Host.ToLowerInvariant(),
            uri.Port);

        return true;
    }

    internal readonly record struct NormalizedOrigin(
        string Scheme,
        string Host,
        int Port);
}

public sealed class BrowserCsrfProtectionOptionsValidator
    : IValidateOptions<BrowserCsrfProtectionOptions>
{
    private readonly IHostEnvironment _environment;

    public BrowserCsrfProtectionOptionsValidator(
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        _environment = environment;
    }

    public ValidateOptionsResult Validate(
        string? name,
        BrowserCsrfProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var browserOrigin = options.BrowserOrigin;

        if (!BrowserCsrfProtectionOptions.IsValidBrowserOrigin(
                browserOrigin))
        {
            return ValidateOptionsResult.Fail(
                "Security:BrowserCsrf:BrowserOrigin must be a valid " +
                "HTTPS Origin, or an HTTP loopback Origin for local development, " +
                "without a path, query, fragment, userinfo, or trailing slash.");
        }

        var uri = new Uri(browserOrigin, UriKind.Absolute);

        var usesHttp =
            string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase);

        // HTTP loopback is permitted only during Development.
        // Staging and Production must always use HTTPS.
        if (usesHttp && !_environment.IsDevelopment())
        {
            return ValidateOptionsResult.Fail(
                "HTTP BrowserOrigin is permitted only in Development. " +
                "Staging and Production require HTTPS.");
        }

        return ValidateOptionsResult.Success;
    }
}
