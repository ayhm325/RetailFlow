using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Auth;

/// <summary>
/// Revalidates AppUser.IsActive on authenticated cookie requests per Microsoft Learn CookieAuthenticationEvents guidance (GAP-008).
/// </summary>
public sealed class RetailFlowCookieEvents : CookieAuthenticationEvents
{
    private readonly RetailFlowDbContext _dbContext;

    public RetailFlowCookieEvents(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var isActive = await _dbContext.AppUsers
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.IsActive)
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (!isActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
