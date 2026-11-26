using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Infrastructure.Catalog;
using RetailFlow.Infrastructure.Identity;
using RetailFlow.Infrastructure.Orders;
using RetailFlow.Infrastructure.Persistence;
using RetailFlow.Infrastructure.Stores;

namespace RetailFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRetailFlowInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<RetailFlowDatabaseOptions>>(sp =>
            new RetailFlowDatabaseOptionsValidator(
                sp.GetRequiredService<IConfiguration>(),
                isDevelopment));

        services
            .AddOptions<RetailFlowDatabaseOptions>()
            .ValidateOnStart();

        services.AddDbContext<RetailFlowDbContext>((sp, options) =>
        {
            var resolvedConfig = sp.GetRequiredService<IConfiguration>();
            var connectionString = ConnectionStringResolver.ResolveRuntime(
                resolvedConfig,
                isDevelopment);

            options.UseSqlServer(connectionString);
        });

        services.AddScoped<IIdentityLookupScopeAccessor, IdentityLookupScopeAccessor>();
        services.AddScoped<IPasswordHasher<RetailFlowIdentityUser>, RetailFlowPasswordHasher>();
        services.AddScoped<IUserValidator<RetailFlowIdentityUser>, RetailFlowUserValidator>();
        services.AddScoped<IUserClaimsPrincipalFactory<RetailFlowIdentityUser>, RetailFlowClaimsPrincipalFactory>();

        services
            .AddIdentityCore<RetailFlowIdentityUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<RetailFlowIdentityRole>()
            .AddEntityFrameworkStores<RetailFlowDbContext>()
            .AddUserStore<ScopedUserStore>()
            .AddClaimsPrincipalFactory<RetailFlowClaimsPrincipalFactory>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<IStoreRequestContextAccessor, StoreRequestContextAccessor>();
        services.AddScoped<IStoreDirectoryResolver, StoreDirectoryResolver>();
        services.AddScoped<IPublicCatalogQueryService, PublicCatalogQueryService>();
        services.AddScoped<IMerchantStoreService, MerchantStoreService>();
        services.AddScoped<IMerchantCatalogService, MerchantCatalogService>();
        services.AddScoped<IPlatformStoreService, PlatformStoreService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }

    private sealed class RetailFlowDatabaseOptions;

    private sealed class RetailFlowDatabaseOptionsValidator(
        IConfiguration configuration,
        bool isDevelopment) : IValidateOptions<RetailFlowDatabaseOptions>
    {
        public ValidateOptionsResult Validate(string? name, RetailFlowDatabaseOptions options)
        {
            try
            {
                _ = ConnectionStringResolver.ResolveRuntime(configuration, isDevelopment);
                return ValidateOptionsResult.Success;
            }
            catch (InvalidOperationException ex)
            {
                return ValidateOptionsResult.Fail(ex.Message);
            }
        }
    }
}
