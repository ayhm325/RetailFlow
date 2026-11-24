
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// OpenAPI
// -----------------------------------------------------------------------------

builder.Services.AddOpenApi();

// -----------------------------------------------------------------------------
// Database
// -----------------------------------------------------------------------------

var connectionString = builder.Configuration
    .GetConnectionString("IdentitySpike")
    ?? throw new InvalidOperationException(
        "Connection string 'IdentitySpike' was not found.");

builder.Services.AddDbContext<IdentitySpikeDbContext>(options =>
    options.UseSqlServer(connectionString));

// -----------------------------------------------------------------------------
// Identity Scope
// -----------------------------------------------------------------------------

builder.Services.AddScoped<
    IIdentityLookupScopeAccessor,
    IdentityLookupScopeAccessor>();

builder.Services.AddScoped<
    IIdentityScopeResolver,
    IdentityScopeResolver>();

// -----------------------------------------------------------------------------
// ASP.NET Core Identity
// -----------------------------------------------------------------------------

builder.Services.AddDataProtection();

builder.Services.AddScoped<ScopedUserStore>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
    })
    .AddUserStore<ScopedUserStoreFacade>()
    .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
        TokenOptions.DefaultProvider);

// -----------------------------------------------------------------------------
// Build Application
// -----------------------------------------------------------------------------

var app = builder.Build();

// -----------------------------------------------------------------------------
// HTTP Request Pipeline
// -----------------------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Development-only database connectivity diagnostic.
    app.MapGet(
        "/diagnostics/database",
        async (
            IdentitySpikeDbContext db,
            CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database
                .CanConnectAsync(cancellationToken);

            if (!canConnect)
            {
                return Results.Problem(
                    title: "Unable to connect to the database.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                status = "Connected",
                database = db.Database.GetDbConnection().Database
            });
        })
        .WithName("CheckDatabaseConnection");
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseMiddleware<IdentityScopeMiddleware>();

// -----------------------------------------------------------------------------
// Run Application
// -----------------------------------------------------------------------------

app.Run();

public partial class Program { }
