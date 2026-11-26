using System.Net;
using System.Net.Mail;

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

    public bool RequireConfiguredInProduction { get; set; }
}

internal sealed class EmailSettingsValidator(
    bool isDevelopment) : IValidateOptions<EmailSettings>
{
    public ValidateOptionsResult Validate(string? name, EmailSettings settings)
    {
        if (!isDevelopment && settings.RequireConfiguredInProduction)
        {
            if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
                string.IsNullOrWhiteSpace(settings.FromAddress) ||
                string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ||
                !Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return ValidateOptionsResult.Fail(
                    "Email:SmtpHost, Email:FromAddress, and a valid Email:PublicBaseUrl are required when Email:RequireConfiguredInProduction is enabled.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}

internal sealed class SmtpEmailSender : IRetailFlowEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailSettings> settings, ILogger<SmtpEmailSender> logger)
    {
        _settings = settings.Value;
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
        if (string.IsNullOrWhiteSpace(_settings.SmtpHost) || string.IsNullOrWhiteSpace(_settings.FromAddress))
        {
            _logger.LogWarning("Email delivery skipped because SMTP settings are not configured. Category={Category}", category);
            return;
        }

        using var message = new MailMessage(_settings.FromAddress, toEmail, subject, body)
        {
            IsBodyHtml = false
        };

        using var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        await client.SendMailAsync(message, cancellationToken);
        _logger.LogInformation("Audit: Security email dispatched via SMTP. Category={Category}", category);
    }
}
