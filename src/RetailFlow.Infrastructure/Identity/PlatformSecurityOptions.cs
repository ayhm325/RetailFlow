namespace RetailFlow.Infrastructure.Identity;

public sealed class PlatformSecurityOptions
{
    /// <summary>
    /// When true, PlatformAdmin accounts must complete MFA enrollment and TOTP sign-in
    /// before accessing privileged /api/platform/stores endpoints.
    /// </summary>
    public bool RequireAdminMfa { get; set; }
}
