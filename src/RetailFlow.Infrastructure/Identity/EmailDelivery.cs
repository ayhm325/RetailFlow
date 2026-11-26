using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RetailFlow.Application.Identity;

namespace RetailFlow.Infrastructure.Identity;

public sealed class EmailSettings
{
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Defaults to true so Production fails closed when SMTP transport or HTTPS PublicBaseUrl is not configured.
    /// </summary>
    public bool RequireConfiguredInProduction { get; set; } = true;
}

internal sealed class EmailSettingsValidator(
    bool isDevelopment) : IValidateOptions<EmailSettings>
{
    public ValidateOptionsResult Validate(string? name, EmailSettings settings)
    {
        if (settings.SmtpPort is < 1 or > 65535)
        {
            return ValidateOptionsResult.Fail("Email:SmtpPort must be between 1 and 65535.");
        }

        if (settings.TimeoutSeconds is < 1 or > 120)
        {
            return ValidateOptionsResult.Fail("Email:TimeoutSeconds must be between 1 and 120.");
        }

        if (!string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
        {
            if (!TryValidatePublicBaseUrl(settings.PublicBaseUrl, requireHttps: !isDevelopment, out var urlError))
            {
                return ValidateOptionsResult.Fail(urlError!);
            }
        }

        if (!isDevelopment && settings.RequireConfiguredInProduction)
        {
            if (string.IsNullOrWhiteSpace(settings.SmtpHost))
            {
                return ValidateOptionsResult.Fail(
                    "Email:SmtpHost is required in Production when Email:RequireConfiguredInProduction is enabled.");
            }

            if (string.IsNullOrWhiteSpace(settings.FromAddress) ||
                !MailAddress.TryCreate(settings.FromAddress.Trim(), out _))
            {
                return ValidateOptionsResult.Fail(
                    "A valid Email:FromAddress is required in Production when Email:RequireConfiguredInProduction is enabled.");
            }

            if (!settings.EnableSsl)
            {
                return ValidateOptionsResult.Fail(
                    "Email:EnableSsl must be true in Production to enforce verified TLS/STARTTLS.");
            }

            string? prodUrlError = null;
            if (string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ||
                !TryValidatePublicBaseUrl(settings.PublicBaseUrl, requireHttps: true, out prodUrlError))
            {
                return ValidateOptionsResult.Fail(
                    prodUrlError ?? "Email:PublicBaseUrl must be a valid HTTPS URL in Production.");
            }

            var hasUser = !string.IsNullOrWhiteSpace(settings.Username);
            var hasPass = !string.IsNullOrWhiteSpace(settings.Password);
            if (hasUser != hasPass)
            {
                return ValidateOptionsResult.Fail(
                    "Email:Username and Email:Password must both be provided when SMTP authentication is configured.");
            }
        }

        return ValidateOptionsResult.Success;
    }

    internal static bool TryValidatePublicBaseUrl(string value, bool requireHttps, out string? error)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            error = "Email:PublicBaseUrl must be an absolute URI.";
            return false;
        }

        if (requireHttps)
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                error = "Email:PublicBaseUrl must use the HTTPS scheme in Production.";
                return false;
            }
        }
        else if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            error = "Email:PublicBaseUrl must use HTTP or HTTPS.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Email:PublicBaseUrl must not contain embedded credentials, query strings, or fragments.";
            return false;
        }

        error = null;
        return true;
    }
}

internal sealed class SmtpEmailSender : IRetailFlowEmailSender
{
    private readonly EmailSettings _settings;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        IOptions<EmailSettings> settings,
        IHostEnvironment environment,
        ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
        _environment = environment;
        _logger = logger;
    }

    public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            "RetailFlow Password Reset",
            $"A password reset was requested for your account. Open the following link to reset your password:\n\n{resetLink}\n\nIf you did not request this, you can safely ignore this email.",
            "PasswordReset",
            cancellationToken);

    public Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken) =>
        SendAsync(
            toEmail,
            "RetailFlow Email Confirmation",
            $"Please confirm your email address by opening the following link:\n\n{confirmationLink}",
            "EmailConfirmation",
            cancellationToken);

    public Task SendSecurityNotificationAsync(string toEmail, string subject, string message, CancellationToken cancellationToken) =>
        SendAsync(toEmail, subject, message, "SecurityNotification", cancellationToken);

    private async Task SendAsync(
        string toEmail,
        string subject,
        string body,
        string category,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.SmtpHost) || string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogInformation(
                    "Email delivery skipped in Development because SMTP settings are not configured. Category={Category}",
                    category);
                return;
            }

            _logger.LogError(
                "Email delivery failed because SMTP settings are missing or disabled in {Environment}. Category={Category}",
                _environment.EnvironmentName,
                category);
            throw new InvalidOperationException(
                "Email delivery is not configured for this environment.");
        }

        if (!_environment.IsDevelopment() && !_settings.EnableSsl)
        {
            throw new InvalidOperationException(
                "SMTP TLS (Email:EnableSsl=true) is required outside Development.");
        }

        using var message = new MailMessage(_settings.FromAddress, toEmail, subject, body)
        {
            IsBodyHtml = false
        };

        var timeoutSeconds = Math.Clamp(_settings.TimeoutSeconds, 1, 120);
        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.EnableSsl,
            Timeout = timeoutSeconds * 1000
        };

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            await client.SendMailAsync(message, timeoutCts.Token);
            _logger.LogInformation("Audit: Security email accepted by SMTP server. Category={Category}", category);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "SMTP delivery timed out after {TimeoutSeconds}s. Category={Category}", timeoutSeconds, category);
            throw new TimeoutException($"SMTP delivery timed out after {timeoutSeconds} seconds.", ex);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP delivery failed with StatusCode={StatusCode}. Category={Category}", ex.StatusCode, category);
            throw new InvalidOperationException("SMTP delivery failed.", ex);
        }
    }
}
