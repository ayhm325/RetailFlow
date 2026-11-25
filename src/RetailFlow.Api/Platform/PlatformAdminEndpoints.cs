using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;

namespace RetailFlow.Api.Platform;

public static class PlatformAdminEndpoints
{
    public static WebApplication MapPlatformAdminEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/platform/stores")
            .RequireAuthorization("PlatformAdmin");

        group.MapPost("", async Task<IResult> (
            CreateStoreCommand command,
            IPlatformStoreService platformService,
            CancellationToken ct) =>
        {
            var store = await platformService.CreateStoreAsync(command, ct);
            return TypedResults.Created($"/api/stores/{store.Slug}", store);
        });

        group.MapPost("/{storeId:guid}/approve", async Task<IResult> (
            Guid storeId,
            IPlatformStoreService platformService,
            CancellationToken ct) =>
        {
            var ok = await platformService.ApproveStoreAsync(storeId, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPost("/{storeId:guid}/reject", async Task<IResult> (
            Guid storeId,
            IPlatformStoreService platformService,
            CancellationToken ct) =>
        {
            var ok = await platformService.RejectStoreAsync(storeId, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPost("/{storeId:guid}/suspend", async Task<IResult> (
            Guid storeId,
            IPlatformStoreService platformService,
            CancellationToken ct) =>
        {
            var ok = await platformService.SuspendStoreAsync(storeId, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPost("/{storeId:guid}/merchant-owner", async Task<IResult> (
            Guid storeId,
            CreateMerchantUserCommand command,
            IAuthService authService,
            CancellationToken ct) =>
        {
            var user = await authService.CreateMerchantOwnerAsync(storeId, command, ct);
            return TypedResults.Created($"/api/platform/stores/{storeId}/merchant-owner", user);
        });

        return app;
    }
}
