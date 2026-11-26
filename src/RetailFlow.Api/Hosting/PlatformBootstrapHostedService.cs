using RetailFlow.Application.Identity;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Auditable, idempotent one-time PlatformAdmin bootstrap from environment/secret configuration (GAP-010).
/// Multi-instance safe via SQL Server sp_getapplock + unique index handling in AuthService.
/// </summary>
public sealed class PlatformBootstrapHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<PlatformBootstrapHostedService> _logger;

    public PlatformBootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PlatformBootstrapHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var adminEmail = _configuration["PlatformBootstrap:AdminEmail"];
        var adminPassword = _configuration["PlatformBootstrap:AdminPassword"];
        var displayName = _configuration["PlatformBootstrap:DisplayName"] ?? "Platform Administrator";
        var requireInProduction = _configuration.GetValue<bool>("PlatformBootstrap:RequireAdminInProduction");

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            if (requireInProduction && _environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "PlatformBootstrap:AdminEmail and PlatformBootstrap:AdminPassword must be configured when RequireAdminInProduction is enabled.");
            }

            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var createdUserId = await authService.EnsurePlatformAdminAsync(
            adminEmail,
            displayName,
            adminPassword,
            cancellationToken);

        if (createdUserId.HasValue)
        {
            _logger.LogInformation(
                "Audit: Initial PlatformAdmin provisioned. UserId={UserId}",
                createdUserId.Value);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
