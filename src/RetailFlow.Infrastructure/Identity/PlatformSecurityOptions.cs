namespace RetailFlow.Infrastructure.Identity;

public sealed class PlatformSecurityOptions
{
    /// <summary>
    /// Defaults to true so PlatformAdmin accounts must complete MFA enrollment and TOTP sign-in
    /// before accessing privileged /api/platform/stores endpoints. Cannot be disabled in Production.
    /// </summary>
    public bool RequireAdminMfa { get; set; } = true;
}
