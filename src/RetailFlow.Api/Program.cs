using RetailFlow.Api.Catalog;
using RetailFlow.Api.Hosting;
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

app.MapPublicCatalogEndpoints();
app.MapProductHostEndpoints();

app.Run();

public partial class Program;
