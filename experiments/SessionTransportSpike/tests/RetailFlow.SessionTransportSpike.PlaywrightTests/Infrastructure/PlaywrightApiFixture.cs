using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Playwright;

using RetailFlow.SessionTransportSpike.Api.Infrastructure.DataProtection;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

public sealed class PlaywrightApiFixture : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> _apiLogs = new();

    private TestHttpsCertificateFactory? _httpsCertificate;
    private TestDataProtectionCertificateFactory? _dataProtectionCertificate;
    private string? _keysDirectory;
    private Process? _apiProcess;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string BaseUrl { get; private set; } = string.Empty;

    public int ApiPort { get; private set; }

    public int InitialDataProtectionKeysCount { get; private set; }

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    public SqlTestDataSeeder Seeder { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var repoRoot = LocateRepositoryRoot();

        var apiProjectDirectory = Path.Combine(
            repoRoot,
            "experiments",
            "SessionTransportSpike",
            "src",
            "RetailFlow.SessionTransportSpike.Api");

        var apiDllPath = LocateBuiltApiDll(apiProjectDirectory);

        ApiPort = GetAvailablePort();
        BaseUrl = $"https://localhost:{ApiPort}";

        _httpsCertificate = TestHttpsCertificateFactory.Create();
        _dataProtectionCertificate =
            TestDataProtectionCertificateFactory.Create();

        _keysDirectory = Path.Combine(
            Path.GetTempPath(),
            "RetailFlow",
            "PlaywrightDataProtectionKeys",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_keysDirectory);

        Seeder = new SqlTestDataSeeder(ConnectionString);
        InitialDataProtectionKeysCount =
            await Seeder.CountDataProtectionKeysAsync();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{apiDllPath}\"",
            WorkingDirectory = apiProjectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["RETAILFLOW_IDENTITY_SPIKE_CONNECTION"] =
            ConnectionString;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["Security__BrowserCsrf__BrowserOrigin"] =
            BaseUrl;
        startInfo.Environment["DataProtection__ApplicationName"] =
            RetailFlowDataProtectionDefaults.ApplicationName;
        startInfo.Environment["DataProtection__KeysDirectory"] =
            _keysDirectory;
        startInfo.Environment["DataProtection__Certificate__Path"] =
            _dataProtectionCertificate.CertificatePath;
        startInfo.Environment["DataProtection__Certificate__Password"] =
            _dataProtectionCertificate.Password;
        startInfo.Environment["Kestrel__Certificates__Default__Path"] =
            _httpsCertificate.CertificatePath;
        startInfo.Environment["Kestrel__Certificates__Default__Password"] =
            _httpsCertificate.Password;

        _apiProcess = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _apiProcess.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                _apiLogs.Enqueue($"[OUT] {args.Data}");
            }
        };

        _apiProcess.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                _apiLogs.Enqueue($"[ERR] {args.Data}");
            }
        };

        if (!_apiProcess.Start())
        {
            throw new InvalidOperationException(
                "Failed to start the SessionTransportSpike API child process.");
        }

        _apiProcess.BeginOutputReadLine();
        _apiProcess.BeginErrorReadLine();

        await WaitForApiReadyAsync();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = true
            });
    }

    public async Task<IBrowserContext> CreateBrowserContextAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException(
                "Playwright browser is not initialized.");
        }

        return await _browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true
            });
    }

    public async Task ExecuteWithDiagnosticsAsync(
        IBrowserContext context,
        string testName,
        Func<Task> action)
    {
        var consoleErrors = new ConcurrentQueue<string>();
        var failedRequests = new ConcurrentQueue<string>();

        void OnPageCreated(object? _, IPage page)
        {
            page.Console += (_, message) =>
            {
                if (string.Equals(
                        message.Type,
                        "error",
                        StringComparison.OrdinalIgnoreCase))
                {
                    consoleErrors.Enqueue(
                        $"[{page.Url}] {message.Text}");
                }
            };

            page.RequestFailed += (_, request) =>
            {
                failedRequests.Enqueue(
                    $"{request.Method} {request.Url} -> {request.Failure}");
            };
        }

        context.Page += OnPageCreated;

        foreach (var existingPage in context.Pages)
        {
            OnPageCreated(context, existingPage);
        }

        await context.Tracing.StartAsync(
            new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = false
            });

        try
        {
            await action();

            await context.Tracing.StopAsync();
        }
        catch (Exception exception)
        {
            var artifactsDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "test-artifacts",
                $"{testName}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");

            Directory.CreateDirectory(artifactsDirectory);

            var tracePath = Path.Combine(artifactsDirectory, "trace.zip");
            await context.Tracing.StopAsync(
                new TracingStopOptions
                {
                    Path = tracePath
                });

            var screenshotPaths = new List<string>();
            var pageIndex = 1;

            foreach (var page in context.Pages)
            {
                try
                {
                    var screenshotPath = Path.Combine(
                        artifactsDirectory,
                        $"page-{pageIndex}.png");

                    await page.ScreenshotAsync(
                        new PageScreenshotOptions
                        {
                            Path = screenshotPath,
                            FullPage = true
                        });

                    screenshotPaths.Add(screenshotPath);
                }
                catch
                {
                    // Page may already be closed.
                }

                pageIndex++;
            }

            var diagnostics = new StringBuilder();
            diagnostics.AppendLine($"Test '{testName}' failed: {exception.Message}");
            diagnostics.AppendLine($"Trace ZIP: {tracePath}");

            if (screenshotPaths.Count > 0)
            {
                diagnostics.AppendLine(
                    $"Screenshots: {string.Join(", ", screenshotPaths)}");
            }

            if (!consoleErrors.IsEmpty)
            {
                diagnostics.AppendLine("Browser Console Errors:");
                foreach (var error in consoleErrors)
                {
                    diagnostics.AppendLine($"  - {error}");
                }
            }

            if (!failedRequests.IsEmpty)
            {
                diagnostics.AppendLine("Browser Failed Requests:");
                foreach (var failed in failedRequests)
                {
                    diagnostics.AppendLine($"  - {failed}");
                }
            }

            diagnostics.AppendLine("Recent API Logs:");
            foreach (var logLine in _apiLogs.TakeLast(40))
            {
                diagnostics.AppendLine($"  {logLine}");
            }

            throw new InvalidOperationException(
                diagnostics.ToString(),
                exception);
        }
        finally
        {
            context.Page -= OnPageCreated;
        }
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        // Stop the API child process before verifying SQL state and deleting temporary files.
        if (_apiProcess is not null)
        {
            try
            {
                if (!_apiProcess.HasExited)
                {
                    _apiProcess.Kill(entireProcessTree: true);
                    await _apiProcess.WaitForExitAsync(
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(10)).Token);
                }

                if (!_apiProcess.HasExited)
                {
                    throw new InvalidOperationException(
                        "SessionTransportSpike API process did not exit within the 10-second shutdown timeout. " +
                        "Temporary Data Protection keys and certificates were not deleted.");
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Failed to stop the SessionTransportSpike API child process cleanly. " +
                    "Temporary Data Protection keys and certificates were not deleted.",
                    exception);
            }
            finally
            {
                _apiProcess.Dispose();
            }
        }

        Exception? verificationError = null;

        try
        {
            if (Seeder is not null)
            {
                var finalCount =
                    await Seeder.CountDataProtectionKeysAsync();

                if (finalCount != InitialDataProtectionKeysCount)
                {
                    verificationError = new InvalidOperationException(
                        "Playwright modified the shared DataProtectionKeys table. " +
                        $"Initial count: {InitialDataProtectionKeysCount}, " +
                        $"Final count: {finalCount}.");
                }
            }
        }
        catch (Exception exception)
        {
            verificationError = exception;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(_keysDirectory))
            {
                try
                {
                    if (Directory.Exists(_keysDirectory))
                    {
                        Directory.Delete(_keysDirectory, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup of temporary Data Protection keys directory.
                }
            }

            _httpsCertificate?.Dispose();
            _dataProtectionCertificate?.Dispose();
        }

        if (verificationError is not null)
        {
            throw new InvalidOperationException(
                "Final DataProtectionKeys isolation verification failed.",
                verificationError);
        }
    }

    private async Task WaitForApiReadyAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var probeUrl =
            $"{BaseUrl}/api/stores/readiness-probe-nonexistent/customers/me";

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_apiProcess!.HasExited)
            {
                throw new InvalidOperationException(
                    $"SessionTransportSpike API exited early with code {_apiProcess.ExitCode}.\n" +
                    string.Join(Environment.NewLine, _apiLogs));
            }

            try
            {
                using var response = await httpClient.GetAsync(probeUrl);
                // Any HTTP response confirms Kestrel + middleware pipeline is listening.
                return;
            }
            catch
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for SessionTransportSpike API at {BaseUrl}.\n" +
            string.Join(Environment.NewLine, _apiLogs));
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var slnxPath = Path.Combine(
                current.FullName,
                "experiments",
                "SessionTransportSpike",
                "RetailFlow.SessionTransportSpike.Playwright.slnx");

            if (File.Exists(slnxPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the RetailFlow repository root.");
    }

    private static string LocateBuiltApiDll(string apiProjectDirectory)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif

        var expectedPath = Path.Combine(
            apiProjectDirectory,
            "bin",
            configuration,
            "net10.0",
            "RetailFlow.SessionTransportSpike.Api.dll");

        if (!File.Exists(expectedPath))
        {
            throw new FileNotFoundException(
                $"Built RetailFlow.SessionTransportSpike.Api.dll for configuration '{configuration}' " +
                $"was not found at '{expectedPath}'. Build the Playwright solution in '{configuration}' configuration first.",
                expectedPath);
        }

        return expectedPath;
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
