using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class EmailHardeningTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public EmailHardeningTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateProductionEmailFactory(Dictionary<string, string?> overrides)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["DataProtection:RequireCertificateInProduction"] = "false"
                    };

                    foreach (var kvp in overrides)
                    {
                        values[kvp.Key] = kvp.Value;
                    }

                    config.AddInMemoryCollection(values);
                });
            });
    }

    [Fact]
    public void TestA_ProductionWithoutSmtp_FailsStartupSafely()
    {
        using var factory = CreateProductionEmailFactory(new Dictionary<string, string?>());

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("Email:", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestB_ProductionWithDisabledEmail_StartsAndSenderDoesNotSilentlySucceed()
    {
        await using var factory = CreateProductionEmailFactory(new Dictionary<string, string?>
        {
            ["Email:RequireConfiguredInProduction"] = "false"
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://retail.example.com")
        });

        var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRetailFlowEmailSender>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendPasswordResetAsync("user@example.com", "https://retail.example.com/reset", CancellationToken.None));
    }

    [Fact]
    public async Task TestC_DevelopmentWithoutSmtp_StartsAndSkipsSafely()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "localhost",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString
                    });
                });
            });

        using var _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRetailFlowEmailSender>();
        await sender.SendPasswordResetAsync("dev@example.com", "http://localhost/reset", CancellationToken.None);
    }

    [Theory]
    [InlineData("", "noreply@retail.example.com", "https://retail.example.com", "true", 587)]
    [InlineData("smtp.example.com", "not-an-email", "https://retail.example.com", "true", 587)]
    [InlineData("smtp.example.com", "noreply@retail.example.com", "http://retail.example.com", "true", 587)]
    [InlineData("smtp.example.com", "noreply@retail.example.com", "https://user:pass@retail.example.com", "true", 587)]
    [InlineData("smtp.example.com", "noreply@retail.example.com", "https://retail.example.com?q=1", "true", 587)]
    [InlineData("smtp.example.com", "noreply@retail.example.com", "/relative", "true", 587)]
    [InlineData("smtp.example.com", "noreply@retail.example.com", "https://retail.example.com", "false", 587)]
    [InlineData("smtp.example.com", "noreply@retail.example.com", "https://retail.example.com", "true", 70000)]
    public void TestD_E_H_InvalidSmtpOrHttpPublicUrlOrDisabledTlsInProduction_Rejected(
        string smtpHost,
        string fromAddress,
        string publicBaseUrl,
        string enableSsl,
        int port)
    {
        using var factory = CreateProductionEmailFactory(new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = smtpHost,
            ["Email:FromAddress"] = fromAddress,
            ["Email:PublicBaseUrl"] = publicBaseUrl,
            ["Email:EnableSsl"] = enableSsl,
            ["Email:SmtpPort"] = port.ToString()
        });

        Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });
    }

    private sealed class CapturingSender : IRetailFlowEmailSender
    {
        public string? LastResetLink { get; private set; }

        public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken)
        {
            LastResetLink = resetLink;
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendSecurityNotificationAsync(string toEmail, string subject, string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task TestH_J_HostHeaderInjectionCannotControlResetLink_And_AntiEnumerationPreserved()
    {
        var capturing = new CapturingSender();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com;evil.attacker",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["DataProtection:RequireCertificateInProduction"] = "false",
                        ["Email:SmtpHost"] = "smtp.retail.example.com",
                        ["Email:SmtpPort"] = "587",
                        ["Email:EnableSsl"] = "true",
                        ["Email:FromAddress"] = "noreply@retail.example.com",
                        ["Email:PublicBaseUrl"] = "https://shop.retail.example.com"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IRetailFlowEmailSender>();
                    services.AddSingleton<IRetailFlowEmailSender>(capturing);
                });
            });

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"eml-{suffix}";
        Guid storeId;
        Guid userId = Guid.Empty;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var platform = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
            var store = await platform.CreateStoreAsync(
                new CreateStoreCommand("Email Store", slug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false),
                CancellationToken.None);
            Assert.True(await platform.ApproveStoreAsync(store.Id, CancellationToken.None));
            storeId = store.Id;
        }

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://retail.example.com"),
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            var email = $"user-{suffix}@test.local";
            using var reg = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(email, "Email User", "EmailPassphrase!2026"));
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
            userId = (await reg.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId;

            // Attempt Host Header Injection
            using var forgotReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/auth/forgot-password")
            {
                Content = JsonContent.Create(new ForgotPasswordCommand(email))
            };
            forgotReq.Headers.Host = "evil.attacker";
            using var forgotResp = await client.SendAsync(forgotReq);
            Assert.Equal(HttpStatusCode.Accepted, forgotResp.StatusCode);

            Assert.NotNull(capturing.LastResetLink);
            Assert.StartsWith("https://shop.retail.example.com/", capturing.LastResetLink, StringComparison.Ordinal);
            Assert.DoesNotContain("evil.attacker", capturing.LastResetLink, StringComparison.OrdinalIgnoreCase);

            // Unknown account returns identical 202 Accepted
            using var unknownResp = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/forgot-password",
                new ForgotPasswordCommand($"unknown-{suffix}@test.local"));
            Assert.Equal(HttpStatusCode.Accepted, unknownResp.StatusCode);
        }
        finally
        {
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task TestI_SmtpConnectionFailureAndCancellation_ObservableInProduction()
    {
        // Bind an unused local port and close it immediately so TCP connect fails deterministically
        int closedPort;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            closedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
        }

        await using var factory = CreateProductionEmailFactory(new Dictionary<string, string?>
        {
            ["Email:SmtpHost"] = "127.0.0.1",
            ["Email:SmtpPort"] = closedPort.ToString(),
            ["Email:EnableSsl"] = "true",
            ["Email:FromAddress"] = "noreply@retail.example.com",
            ["Email:PublicBaseUrl"] = "https://shop.retail.example.com",
            ["Email:TimeoutSeconds"] = "2"
        });

        using var _ = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<IRetailFlowEmailSender>();

        // 1. Pre-cancelled token propagates OperationCanceledException
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendPasswordResetAsync("user@example.com", "https://shop.retail.example.com/reset", cts.Token));

        // 2. Closed SMTP port fails observably (never silently returns success)
        await Assert.ThrowsAnyAsync<Exception>(() =>
            sender.SendPasswordResetAsync("user@example.com", "https://shop.retail.example.com/reset", CancellationToken.None));
    }
}
