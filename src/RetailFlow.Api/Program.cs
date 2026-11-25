using RetailFlow.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProductHostInfrastructure(
    builder.Configuration,
    builder.Environment);

var app = builder.Build();

app.UseProductHostPipeline();

app.MapProductHostEndpoints();

app.Run();

public partial class Program;
