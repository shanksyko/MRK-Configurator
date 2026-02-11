using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.App.Config;
using Mieruka.App.Services.Ui;
using Mieruka.Automation.Login;
using Mieruka.Automation.Tabs;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Core.Services;
using OpenQA.Selenium;
using Serilog;

namespace Mieruka.App.Services.Testing;

internal sealed class SiteTestService
{
    private readonly ConfiguratorWorkspace _workspace;
    private readonly IDisplayService? _displayService;
    private readonly ITelemetry _telemetry;
    private readonly ILogger _logger = Log.ForContext<SiteTestService>();

    public SiteTestService(ConfiguratorWorkspace workspace, IDisplayService? displayService, ITelemetry telemetry)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _displayService = displayService;
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    public async Task<TestRunResult> TestAsync(SiteConfig site, string? selectedMonitorStableId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(site);

        var connectivityResult = await CheckConnectivityAsync(site.Url, ct).ConfigureAwait(false);
        if (connectivityResult is not null)
        {
            _logger.Warning("Connectivity check failed for {Url}: {Message}", site.Url, connectivityResult);
        }

        return await (RequiresSelenium(site)
            ? TestWithSeleniumAsync(site, selectedMonitorStableId, ct)
            : TestWithProcessAsync(site, selectedMonitorStableId, ct)).ConfigureAwait(false);
    }

    private async Task<string?> CheckConnectivityAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null; // Skip check for invalid/empty URLs
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return null; // Reachable
        }
        catch (HttpRequestException ex)
        {
            return $"Não foi possível conectar em {uri.Host}: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return $"Timeout ao conectar em {uri.Host}";
        }
        catch (Exception ex)
        {
            return $"Erro de conexão: {ex.Message}";
        }
    }

    private async Task<TestRunResult> TestWithProcessAsync(SiteConfig site, string? selectedMonitorStableId, CancellationToken ct)
    {
        var monitor = WindowPlacementHelper.ResolveTargetMonitor(site, selectedMonitorStableId, _workspace.Monitors, _displayService);
        var zone = WindowPlacementHelper.ResolveTargetZone(monitor, site.TargetZonePresetId, site.Window, _workspace.ZonePresets);

        var executable = ResolveBrowserExecutable(site.Browser);
        var arguments = BuildBrowserArgumentString(site);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return new TestRunResult(false, false, monitor, zone, "Falha ao iniciar o navegador.");
        }

        var topMost = site.Window.AlwaysOnTop || (zone.WidthPercentage >= 99.5 && zone.HeightPercentage >= 99.5);
        await WindowPlacementHelper
            .ForcePlaceProcessWindowAsync(process, monitor, zone, topMost, ct)
            .ConfigureAwait(false);

        process.Refresh();
        var hasWindow = process.MainWindowHandle != IntPtr.Zero;
        return new TestRunResult(true, hasWindow, monitor, zone, hasWindow ? null : "O navegador foi iniciado, mas a janela não foi encontrada.");
    }

    private async Task<TestRunResult> TestWithSeleniumAsync(SiteConfig site, string? selectedMonitorStableId, CancellationToken ct)
    {
        IWebDriver? driver = null;
        var monitor = WindowPlacementHelper.ResolveTargetMonitor(site, selectedMonitorStableId, _workspace.Monitors, _displayService);
        var zone = WindowPlacementHelper.ResolveTargetZone(monitor, site.TargetZonePresetId, site.Window, _workspace.ZonePresets);

        try
        {
            var arguments = CollectBrowserArguments(site).ToList();
            driver = WebDriverFactory.Create(site, arguments);
            ApplyWhitelist(driver, site.AllowedTabHosts ?? Array.Empty<string>());
            await ExecuteLoginAsync(driver, site.Login, ct).ConfigureAwait(false);

            var bounds = WindowPlacementHelper.CalculateZoneBounds(monitor, zone);
            if (OperatingSystem.IsWindows())
            {
                var handleString = driver.CurrentWindowHandle;
                if (long.TryParse(handleString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handleValue))
                {
                    var handle = new IntPtr(handleValue);
                    WindowPlacementHelper.PlaceWindow(handle, monitor, zone, site.Window.AlwaysOnTop);
                }
                else
                {
                    driver.Manage().Window.Position = new System.Drawing.Point(bounds.Left, bounds.Top);
                    driver.Manage().Window.Size = new System.Drawing.Size(bounds.Width, bounds.Height);
                }
            }
            else
            {
                driver.Manage().Window.Position = new System.Drawing.Point(bounds.Left, bounds.Top);
                driver.Manage().Window.Size = new System.Drawing.Size(bounds.Width, bounds.Height);
            }

            return new TestRunResult(true, true, monitor, zone, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Erro ao testar site via Selenium {SiteId}.", site.Id);
            return new TestRunResult(false, false, monitor, zone, $"Falha ao iniciar o Selenium: {ex.Message}");
        }
        finally
        {
            driver?.Quit();
        }
    }

    private static bool RequiresSelenium(SiteConfig site)
    {
        if (site.Login is { } login)
        {
            if (!string.IsNullOrWhiteSpace(login.Username)
                || !string.IsNullOrWhiteSpace(login.Password)
                || !string.IsNullOrWhiteSpace(login.UserSelector)
                || !string.IsNullOrWhiteSpace(login.PassSelector)
                || !string.IsNullOrWhiteSpace(login.SubmitSelector)
                || !string.IsNullOrWhiteSpace(login.Script))
            {
                return true;
            }
        }

        return site.AllowedTabHosts?.Any(host => !string.IsNullOrWhiteSpace(host)) == true;
    }

    private void ApplyWhitelist(IWebDriver driver, IEnumerable<string> hosts)
    {
        var sanitized = hosts?.Where(static host => !string.IsNullOrWhiteSpace(host)).ToList();
        if (sanitized is null || sanitized.Count == 0)
        {
            return;
        }

        var tabManager = new TabManager(_telemetry);
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _ = Task.Run(() => tabManager.MonitorAsync(driver, sanitized, cancellation.Token));
    }

    private async Task ExecuteLoginAsync(IWebDriver driver, LoginProfile? login, CancellationToken ct)
    {
        if (login is null)
        {
            return;
        }

        var loginService = new LoginService(_telemetry);
        try
        {
            await loginService.TryLoginAsync(driver, login, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Falha durante a automação de login.");
        }
    }

    private string ResolveBrowserExecutable(BrowserType browser)
    {
        if (OperatingSystem.IsWindows())
        {
            return browser switch
            {
                BrowserType.Chrome => "chrome.exe",
                BrowserType.Edge => "msedge.exe",
                _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
            };
        }

        return browser switch
        {
            BrowserType.Chrome => "google-chrome",
            BrowserType.Edge => "microsoft-edge",
            _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
        };
    }

    private string BuildBrowserArgumentString(SiteConfig site)
    {
        var arguments = CollectBrowserArguments(site).ToList();
        if (!site.AppMode && !string.IsNullOrWhiteSpace(site.Url))
        {
            arguments.Add(site.Url);
        }

        return string.Join(' ', arguments);
    }

    private IEnumerable<string> CollectBrowserArguments(SiteConfig site)
    {
        var arguments = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddArgument(string? argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return;
            }

            if (seen.Add(argument))
            {
                arguments.Add(argument);
            }
        }

        foreach (var argument in GetGlobalBrowserArguments(site.Browser))
        {
            AddArgument(argument);
        }

        foreach (var argument in site.BrowserArguments ?? Array.Empty<string>())
        {
            AddArgument(argument);
        }

        if (!string.IsNullOrWhiteSpace(site.UserDataDirectory) && !ContainsArgument(arguments, "--user-data-dir", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--user-data-dir", site.UserDataDirectory));
        }

        if (!string.IsNullOrWhiteSpace(site.ProfileDirectory) && !ContainsArgument(arguments, "--profile-directory", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--profile-directory", site.ProfileDirectory));
        }

        if (site.KioskMode && !ContainsArgument(arguments, "--kiosk"))
        {
            AddArgument("--kiosk");
        }

        if (site.AppMode)
        {
            if (!string.IsNullOrWhiteSpace(site.Url) && !ContainsArgument(arguments, "--app", matchByPrefix: true))
            {
                AddArgument(FormatArgument("--app", site.Url));
            }
        }

        return arguments;
    }

    private IEnumerable<string> GetGlobalBrowserArguments(BrowserType browser)
    {
        return browser switch
        {
            BrowserType.Chrome => _workspace.BrowserArguments.Chrome ?? Array.Empty<string>(),
            BrowserType.Edge => _workspace.BrowserArguments.Edge ?? Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
    }

    private static bool ContainsArgument(IEnumerable<string> arguments, string name, bool matchByPrefix = false)
    {
        foreach (var argument in arguments)
        {
            if (matchByPrefix)
            {
                if (argument.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatArgument(string name, string value)
    {
        var sanitized = value.Replace("\"", "\\\"");
        return $"{name}=\"{sanitized}\"";
    }
}
