using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
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
/// Credentials are resolved from the Core credential vault via <see cref="SecretsProvider"/>.
/// </summary>
public sealed class LoginOrchestrator
{
    private static readonly ILogger Logger = Log.ForContext<LoginOrchestrator>();
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ITelemetry _telemetry;
    private readonly SecretsProvider _secrets;

    public LoginOrchestrator(SecretsProvider secrets, ITelemetry? telemetry = null)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _telemetry = telemetry ?? NullTelemetry.Instance;
    }

    /// <summary>
    /// Ensures the user is logged in to the specified site.
    /// Returns <c>true</c> when login succeeds or is not required, <c>false</c> otherwise.
    /// </summary>
    /// <param name="site">Site configuration describing the target page.</param>
    /// <param name="browserArguments">Optional browser arguments collected by the caller.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<bool> EnsureLoggedInAsync(
        SiteConfig site,
        IReadOnlyList<string>? browserArguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Login is null)
        {
            return true;
        }

        var profile = ResolveCredentials(site);
        if (profile is null)
        {
            Logger.Debug("No login credentials or selectors configured for site {SiteId}; skipping.", site.Id);
            return true;
        }

        IWebDriver? driver = null;
        try
        {
            driver = CreateDriver(site.Browser, browserArguments);

            if (!string.IsNullOrWhiteSpace(site.Url))
            {
                driver.Navigate().GoToUrl(site.Url);
            }

            var loginService = new LoginService(_telemetry);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(DefaultTimeout);

            var result = await loginService.TryLoginAsync(driver, profile, cts.Token).ConfigureAwait(false);

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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Warning("Login timed out for site {SiteId}.", site.Id);
            _telemetry.Warn($"Login timed out for site '{site.Id}'.");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Login failed for site {SiteId}.", site.Id);
            _telemetry.Warn($"Login failed for site '{site.Id}': {ex.Message}", ex);
            return false;
        }
        finally
        {
            SafeDispose(driver);
        }
    }

    private LoginProfile? ResolveCredentials(SiteConfig site)
    {
        var login = site.Login;
        if (login is null)
        {
            return null;
        }

        var username = SecureStringToString(_secrets.GetUsernameFor(site.Id));
        var password = SecureStringToString(_secrets.GetPasswordFor(site.Id));

        // If no vault credentials and no selectors/script configured, nothing to do.
        if (string.IsNullOrWhiteSpace(username) &&
            string.IsNullOrWhiteSpace(password) &&
            string.IsNullOrWhiteSpace(login.UserSelector) &&
            string.IsNullOrWhiteSpace(login.PassSelector) &&
            string.IsNullOrWhiteSpace(login.Script))
        {
            return null;
        }

        return login with
        {
            Username = username ?? login.Username,
            Password = password ?? login.Password,
        };
    }

    private static string? SecureStringToString(SecureString? secureString)
    {
        if (secureString is null || secureString.Length == 0)
        {
            return null;
        }

        var ptr = IntPtr.Zero;
        try
        {
            ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }

    private static IWebDriver CreateDriver(BrowserType browser, IReadOnlyList<string>? arguments)
    {
        return browser switch
        {
            BrowserType.Chrome => CreateChromeDriver(arguments),
            BrowserType.Edge => CreateEdgeDriver(arguments),
            _ => throw new NotSupportedException($"Browser '{browser}' is not supported for automated login."),
        };
    }

    private static IWebDriver CreateChromeDriver(IReadOnlyList<string>? arguments)
    {
        var options = new ChromeOptions();
        if (arguments is not null)
        {
            foreach (var arg in arguments)
            {
                options.AddArgument(arg);
            }
        }

        var service = ChromeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;
        try
        {
            return new ChromeDriver(service, options);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    private static IWebDriver CreateEdgeDriver(IReadOnlyList<string>? arguments)
    {
        var options = new EdgeOptions();
        if (arguments is not null)
        {
            foreach (var arg in arguments)
            {
                options.AddArgument(arg);
            }
        }

        var service = EdgeDriverService.CreateDefaultService();
        service.HideCommandPromptWindow = true;
        try
        {
            return new EdgeDriver(service, options);
        }
        catch
        {
            service.Dispose();
            throw;
        }
    }

    private static void SafeDispose(IWebDriver? driver)
    {
        if (driver is null) return;
        try { driver.Quit(); } catch { /* best-effort */ }
        try { driver.Dispose(); } catch { /* best-effort */ }
    }
}
