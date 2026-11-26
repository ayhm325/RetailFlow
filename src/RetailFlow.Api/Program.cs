using RetailFlow.Api.Auth;
using RetailFlow.Api.Catalog;
using RetailFlow.Api.Hosting;
using RetailFlow.Api.Merchant;
using RetailFlow.Api.Orders;
using RetailFlow.Api.Platform;
using RetailFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProductHostInfrastructure(
    builder.Configuration,
    builder.Environment);
builder.Services.AddRetailFlowInfrastructure(
    builder.Configuration,
    builder.Environment.IsDevelopment());

var app = builder.Build();

app.UseProductHostPipeline();

app.MapAuthEndpoints();
app.MapPublicCatalogEndpoints();
app.MapMerchantEndpoints();
app.MapCustomerOrderEndpoints();
app.MapPlatformAdminEndpoints();
app.MapProductHostEndpoints();

app.Run();

public partial class Program;
