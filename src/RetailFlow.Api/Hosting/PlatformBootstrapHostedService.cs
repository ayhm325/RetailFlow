using RetailFlow.Application.Identity;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Auditable, idempotent one-time PlatformAdmin bootstrap from environment/secret configuration (GAP-010).
/// </summary>
public sealed class PlatformBootstrapHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformBootstrapHostedService> _logger;

    public PlatformBootstrapHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PlatformBootstrapHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var adminEmail = _configuration["PlatformBootstrap:AdminEmail"];
        var adminPassword = _configuration["PlatformBootstrap:AdminPassword"];
        var displayName = _configuration["PlatformBootstrap:DisplayName"] ?? "Platform Administrator";

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
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
