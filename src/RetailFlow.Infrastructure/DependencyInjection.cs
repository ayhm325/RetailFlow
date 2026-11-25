using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var envConnection = Environment.GetEnvironmentVariable("RETAILFLOW_CONNECTION");
        var configConnection = configuration.GetConnectionString("RetailFlow");
        var connectionString = !string.IsNullOrWhiteSpace(envConnection)
            ? envConnection
            : !string.IsNullOrWhiteSpace(configConnection)
                ? configConnection
                : RetailFlowDbContextFactory.DefaultLocalConnectionString;

        services.AddDbContext<RetailFlowDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IStoreRequestContextAccessor, StoreRequestContextAccessor>();
        services.AddScoped<IStoreDirectoryResolver, StoreDirectoryResolver>();
        services.AddScoped<IPublicCatalogQueryService, PublicCatalogQueryService>();

        return services;
    }
}
