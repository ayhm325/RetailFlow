using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Stores;
using RetailFlow.Infrastructure.Catalog;
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

        services.AddScoped<IStoreRequestContextAccessor, StoreRequestContextAccessor>();
        services.AddScoped<IStoreDirectoryResolver, StoreDirectoryResolver>();
        services.AddScoped<IPublicCatalogQueryService, PublicCatalogQueryService>();
        services.AddScoped<IMerchantStoreService, MerchantStoreService>();
        services.AddScoped<IMerchantCatalogService, MerchantCatalogService>();

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
