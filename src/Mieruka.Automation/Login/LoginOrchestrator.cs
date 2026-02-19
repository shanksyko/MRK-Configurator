using System;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Mieruka.Core.Services;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using Serilog;

namespace Mieruka.Automation.Login;

/// <summary>
/// Coordinates automated login flows for sites that require authentication.
/// </summary>
public sealed class LoginOrchestrator
{
    private static readonly ILogger Logger = Log.ForContext<LoginOrchestrator>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ITelemetry _telemetry;
    private readonly CredentialVault _credentialVault;

    public LoginOrchestrator()
        : this(NullTelemetry.Instance, new CredentialVault("Mieruka"))
    {
    }

    public LoginOrchestrator(ITelemetry telemetry, CredentialVault credentialVault)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _credentialVault = credentialVault ?? throw new ArgumentNullException(nameof(credentialVault));
    }

    /// <summary>
    /// Ensures the user is logged in to the specified site.
    /// Returns <c>true</c> when login succeeds or is not required, <c>false</c> otherwise.
    /// </summary>
    public bool EnsureLoggedIn(SiteConfig site)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Login is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(site.Login.Username) &&
            string.IsNullOrWhiteSpace(site.Login.UserSelector) &&
            string.IsNullOrWhiteSpace(site.Login.Script))
        {
            Logger.Debug("No login credentials or selectors configured for site {SiteId}; skipping.", site.Id);
            return true;
        }

        IWebDriver? driver = null;
        try
        {
            driver = CreateDriver(site);

            if (!string.IsNullOrWhiteSpace(site.Url))
            {
                driver.Navigate().GoToUrl(site.Url);
            }

            var loginService = new LoginService(_telemetry, _credentialVault);

            using var cts = new CancellationTokenSource(DefaultTimeout);
            var result = loginService.TryLoginAsync(driver, site.Login, cts.Token)
                .GetAwaiter()
                .GetResult();

            if (result)
            {
                Logger.Information("Login succeeded for site {SiteId}.", site.Id);
            }
            else
            {
                Logger.Warning("Login returned false for site {SiteId}.", site.Id);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Login timed out for site {SiteId}.", site.Id);
            _telemetry.Warn($"Login timed out for site '{site.Id}'.");
            SafeDispose(driver);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Login failed for site {SiteId}.", site.Id);
            _telemetry.Warn($"Login failed for site '{site.Id}': {ex.Message}", ex);
            SafeDispose(driver);
            return false;
        }
    }

    private static IWebDriver CreateDriver(SiteConfig site)
    {
        return site.Browser switch
        {
            BrowserType.Chrome => new ChromeDriver(),
            BrowserType.Edge => new EdgeDriver(),
            _ => throw new NotSupportedException($"Browser '{site.Browser}' is not supported for automated login."),
        };
    }

    private static void SafeDispose(IWebDriver? driver)
    {
        if (driver is null) return;
        try { driver.Quit(); } catch { /* best-effort */ }
        try { driver.Dispose(); } catch { /* best-effort */ }
    }
}
