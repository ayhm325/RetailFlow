using System.Net;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

internal sealed class BrowserAttackerServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string Origin { get; }

    private BrowserAttackerServer(
        WebApplication app,
        string origin)
    {
        _app = app;
        Origin = origin;
    }

    public static async Task<BrowserAttackerServer> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
        });

        var app = builder.Build();

        app.MapGet("/attack-login", (HttpRequest request) =>
        {
            var action = request.Query["action"].ToString();
            var email = request.Query["email"].ToString();
            var password = request.Query["password"].ToString();

            var encoder = HtmlEncoder.Default;

            var html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <title>Attacker Login Form</title>
                </head>
                <body>
                    <form id="attackForm"
                          method="POST"
                          action="{{encoder.Encode(action)}}">
                        <input type="hidden" name="Email" value="{{encoder.Encode(email)}}" />
                        <input type="hidden" name="Password" value="{{encoder.Encode(password)}}" />
                    </form>
                    <script>
                        document.getElementById("attackForm").submit();
                    </script>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/attack-logout", (HttpRequest request) =>
        {
            var action = request.Query["action"].ToString();
            var encoder = HtmlEncoder.Default;

            var html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <title>Attacker Logout Form</title>
                </head>
                <body>
                    <form id="attackForm"
                          method="POST"
                          action="{{encoder.Encode(action)}}">
                    </form>
                    <script>
                        document.getElementById("attackForm").submit();
                    </script>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8");
        });

        await app.StartAsync(cancellationToken);

        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Could not determine the attacker server address.");

        var origin = new Uri(address).GetLeftPart(UriPartial.Authority);

        return new BrowserAttackerServer(app, origin);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
