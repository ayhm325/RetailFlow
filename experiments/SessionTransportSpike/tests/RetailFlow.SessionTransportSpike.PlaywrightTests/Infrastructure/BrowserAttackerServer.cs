using System.Collections.Concurrent;
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
    private readonly TestHttpsCertificateFactory? _httpsCertificate;

    private readonly ConcurrentDictionary<string, LoginAttackPayload>
        _loginPayloads = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string>
        _logoutTargets = new(StringComparer.Ordinal);

    public string Origin { get; }

    private BrowserAttackerServer(
        WebApplication app,
        string origin,
        TestHttpsCertificateFactory? httpsCertificate)
    {
        _app = app;
        Origin = origin;
        _httpsCertificate = httpsCertificate;
    }

    public static Task<BrowserAttackerServer> StartHttpAsync(
        CancellationToken cancellationToken = default)
    {
        return StartAsync(useHttpsLocalhost: false, cancellationToken);
    }

    public static Task<BrowserAttackerServer> StartHttpsLocalhostAsync(
        CancellationToken cancellationToken = default)
    {
        return StartAsync(useHttpsLocalhost: true, cancellationToken);
    }

    public string RegisterLoginAttack(
        string actionUrl,
        string email,
        string password)
    {
        var id = Guid.NewGuid().ToString("N");
        var payloads = _sharedLoginPayloads ?? _loginPayloads;
        payloads[id] = new LoginAttackPayload(actionUrl, email, password);
        return $"{Origin}/attack-login/{id}";
    }

    public string RegisterLogoutAttack(string actionUrl)
    {
        var id = Guid.NewGuid().ToString("N");
        var targets = _sharedLogoutTargets ?? _logoutTargets;
        targets[id] = actionUrl;
        return $"{Origin}/attack-logout/{id}";
    }

    private static async Task<BrowserAttackerServer> StartAsync(
        bool useHttpsLocalhost,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();

        TestHttpsCertificateFactory? httpsCertificate = null;

        if (useHttpsLocalhost)
        {
            httpsCertificate = TestHttpsCertificateFactory.Create();

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(
                    IPAddress.Loopback,
                    0,
                    listenOptions =>
                    {
                        listenOptions.UseHttps(
                            httpsCertificate.CertificatePath,
                            httpsCertificate.Password);
                    });
            });
        }
        else
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
            });
        }

        var app = builder.Build();

        var loginPayloads = new ConcurrentDictionary<string, LoginAttackPayload>(
            StringComparer.Ordinal);
        var logoutTargets = new ConcurrentDictionary<string, string>(
            StringComparer.Ordinal);

        app.MapGet("/attack-login/{id}", (string id) =>
        {
            if (!loginPayloads.TryRemove(id, out var payload))
            {
                return Results.NotFound();
            }

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
                          action="{{encoder.Encode(payload.ActionUrl)}}">
                        <input type="hidden" name="Email" value="{{encoder.Encode(payload.Email)}}" />
                        <input type="hidden" name="Password" value="{{encoder.Encode(payload.Password)}}" />
                    </form>
                    <script>
                        document.getElementById("attackForm").submit();
                    </script>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/attack-logout/{id}", (string id) =>
        {
            if (!logoutTargets.TryRemove(id, out var actionUrl))
            {
                return Results.NotFound();
            }

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
                          action="{{encoder.Encode(actionUrl)}}">
                    </form>
                    <script>
                        document.getElementById("attackForm").submit();
                    </script>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8");
        });

        try
        {
            await app.StartAsync(cancellationToken);

            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>();
            var address = addresses?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Could not determine the attacker server address.");

            var rawUri = new Uri(address);
            var origin = useHttpsLocalhost
                ? $"https://localhost:{rawUri.Port}"
                : rawUri.GetLeftPart(UriPartial.Authority);

            var instance = new BrowserAttackerServer(
                app,
                origin,
                httpsCertificate);

            // Share the endpoint dictionaries with the instance.
            instance.ShareState(loginPayloads, logoutTargets);

            return instance;
        }
        catch
        {
            httpsCertificate?.Dispose();
            await app.DisposeAsync();
            throw;
        }
    }

    private ConcurrentDictionary<string, LoginAttackPayload>? _sharedLoginPayloads;
    private ConcurrentDictionary<string, string>? _sharedLogoutTargets;

    private void ShareState(
        ConcurrentDictionary<string, LoginAttackPayload> loginPayloads,
        ConcurrentDictionary<string, string> logoutTargets)
    {
        _sharedLoginPayloads = loginPayloads;
        _sharedLogoutTargets = logoutTargets;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _httpsCertificate?.Dispose();
    }

    private sealed record LoginAttackPayload(
        string ActionUrl,
        string Email,
        string Password);
}
