using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.SessionTransportSpike.Authentication;

/// <summary>
/// Selects a StoreCustomer authentication cookie from the
/// server-established IdentityLookupScope.
///
/// This component does not authenticate a ticket or authorize
/// a user. Those responsibilities remain with ASP.NET Core
/// authentication and tenant validation.
/// </summary>
public sealed class StoreCustomerCookieManager : ICookieManager
{
    public const string BaseCookieName =
        "__Secure-RetailFlow.StoreCustomer";

    private readonly ChunkingCookieManager _inner;

    public StoreCustomerCookieManager()
    {
        _inner = new ChunkingCookieManager();
    }

    /// <summary>
    /// Reads only the cookie belonging to the current
    /// StoreCustomer IdentityLookupScope.
    ///
    /// A request without a valid StoreCustomer scope
    /// is treated as unauthenticated.
    /// </summary>
    public string? GetRequestCookie(
        HttpContext context,
        string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateKey(key);

        if (!TryResolveCookie(
                context,
                key,
                out var descriptor))
        {
            return null;
        }

        return _inner.GetRequestCookie(
            context,
            descriptor.Name);
    }

    /// <summary>
    /// Issues the StoreCustomer cookie using the tenant-specific
    /// name and API path.
    /// </summary>
    public void AppendResponseCookie(
        HttpContext context,
        string key,
        string? value,
        CookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ValidateKey(key);

        var descriptor = ResolveRequiredCookie(
            context,
            key,
            nameof(AppendResponseCookie));

        var scopedOptions = new CookieOptions(options)
        {
            Path = descriptor.Path
        };

        _inner.AppendResponseCookie(
            context,
            descriptor.Name,
            value,
            scopedOptions);
    }

    /// <summary>
    /// Deletes the same tenant-specific cookie that was
    /// previously issued, including the matching path.
    /// </summary>
    public void DeleteCookie(
        HttpContext context,
        string key,
        CookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ValidateKey(key);

        var descriptor = ResolveRequiredCookie(
            context,
            key,
            nameof(DeleteCookie));

        var scopedOptions = new CookieOptions(options)
        {
            Path = descriptor.Path
        };

        _inner.DeleteCookie(
            context,
            descriptor.Name,
            scopedOptions);
    }

    private static CookieDescriptor ResolveRequiredCookie(
        HttpContext context,
        string key,
        string operation)
    {
        if (TryResolveCookie(context, key, out var descriptor))
        {
            return descriptor;
        }

        throw new InvalidOperationException(
            $"Cannot execute {operation}: " +
            "a valid StoreCustomer IdentityLookupScope " +
            "was not established for this request.");
    }

    private static bool TryResolveCookie(
        HttpContext context,
        string baseCookieName,
        out CookieDescriptor descriptor)
    {
        descriptor = default!;

        // The endpoint must explicitly declare StoreCustomer scope.
        var metadata = context.GetEndpoint()?
            .Metadata
            .GetMetadata<IdentityScopeMetadata>();

        if (metadata?.AccountKind != AccountKind.StoreCustomer)
        {
            return false;
        }

        // The scope must come from the server-side middleware.
        var scopeAccessor = context.RequestServices
            .GetService<IIdentityLookupScopeAccessor>();

        var currentScope = scopeAccessor?.Current;

        if (currentScope is null ||
            currentScope.AccountKind != AccountKind.StoreCustomer ||
            currentScope.StoreId is not Guid storeId ||
            storeId == Guid.Empty)
        {
            return false;
        }

        // Use the slug resolved for this routed request.
        if (!context.Request.RouteValues.TryGetValue(
                "slug",
                out var slugValue) ||
            slugValue is not string slug ||
            string.IsNullOrWhiteSpace(slug) ||
            slug.Length > 100)
        {
            return false;
        }

        // StoreId makes the cookie name stable even if the
        // public StoreSlug changes later.
        var cookieName =
            $"{baseCookieName}.store.{storeId:N}";

        // PathBase must be retained when the API is hosted
        // behind a configured application path prefix.
        var pathBase = context.Request.PathBase.Value?
            .TrimEnd('/')
            ?? string.Empty;

        var escapedSlug = Uri.EscapeDataString(slug);

        var cookiePath =
            $"{pathBase}/api/stores/{escapedSlug}/customers";

        descriptor = new CookieDescriptor(
            cookieName,
            cookiePath);

        return true;
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
    }

    private sealed record CookieDescriptor(
        string Name,
        string Path);
}
