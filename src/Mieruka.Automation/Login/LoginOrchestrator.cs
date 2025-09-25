using System;
using System.Threading;
using Mieruka.Automation.Drivers;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Mieruka.Core.Services;
using OpenQA.Selenium;

namespace Mieruka.Automation.Login;

public sealed class LoginOrchestrator
{
    private readonly BrowserLauncher _browserLauncher;
    private readonly LoginService _loginService;
    private readonly CookieBridge _cookieBridge;
    private readonly SessionVerifier _sessionVerifier;
    private readonly UrlAllowlist? _allowlist;
    private readonly ITelemetry _telemetry;
    private readonly AuditLog? _auditLog;

    public LoginOrchestrator(
        BrowserLauncher? browserLauncher = null,
        LoginService? loginService = null,
        CookieBridge? cookieBridge = null,
        SessionVerifier? sessionVerifier = null,
        UrlAllowlist? allowlist = null,
        ITelemetry? telemetry = null,
        AuditLog? auditLog = null)
    {
        _telemetry = telemetry ?? NullTelemetry.Instance;
        _allowlist = allowlist;
        _auditLog = auditLog;

        _browserLauncher = browserLauncher ?? new BrowserLauncher(new WebDriverFactory());
        _loginService = loginService ?? new LoginService(_telemetry);

        _cookieBridge = cookieBridge ?? new CookieBridge(new CookieSafeStore(), _telemetry, _allowlist);
        _sessionVerifier = sessionVerifier ?? new SessionVerifier(_telemetry, _allowlist);
    }

    public bool EnsureLoggedIn(SiteConfig site)
    {
        ArgumentNullException.ThrowIfNull(site);

        if (site.Login is not { } loginProfile)
        {
            _telemetry.Warn($"Site '{site.Id}' does not define a login profile.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(site.Url) || !Uri.TryCreate(site.Url, UriKind.Absolute, out var targetUri))
        {
            _telemetry.Warn($"Site '{site.Id}' has an invalid or empty URL.");
            return false;
        }

        if (_allowlist is not null && !_allowlist.IsAllowed(targetUri, site.Id))
        {
            _telemetry.Warn($"Navigation to '{targetUri}' is blocked by the allowlist.");
            return false;
        }

        var success = false;

        try
        {
            using var driver = _browserLauncher.Launch(site);
            NavigateToTarget(driver, targetUri);

            var restored = _cookieBridge.RestoreCookies(driver, site, targetUri);
            if (restored)
            {
                RefreshSilently(driver);
            }

            using var cts = CreateCancellation(loginProfile);
            try
            {
                _ = _loginService.TryLoginAsync(driver, loginProfile, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                _telemetry.Warn($"Login automation timed out for site '{site.Id}'.");
            }

            success = _sessionVerifier.Verify(driver, site, targetUri);

            if (success)
            {
                _cookieBridge.PersistCookies(driver, site, targetUri);
            }

            return success;
        }
        catch (WebDriverException exception)
        {
            _telemetry.Error($"Selenium automation failed for site '{site.Id}'.", exception);
            return false;
        }
        catch (Exception exception)
        {
            _telemetry.Error($"Unexpected failure while executing login automation for site '{site.Id}'.", exception);
            return false;
        }
        finally
        {
            _auditLog?.RecordLoginAttempt(site.Id, success);
        }
    }

    private static CancellationTokenSource CreateCancellation(LoginProfile profile)
    {
        var timeout = Math.Max(profile.TimeoutSeconds, 1);
        return new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
    }

    private static void NavigateToTarget(IWebDriver driver, Uri target)
    {
        string? current = null;
        try
        {
            current = driver.Url;
        }
        catch (WebDriverException)
        {
            // Ignore and navigate directly.
        }

        if (!string.Equals(current, target.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                driver.Navigate().GoToUrl(target);
            }
            catch (WebDriverException)
            {
                // Propagate so the caller can handle the failure.
                throw;
            }
        }
    }

    private static void RefreshSilently(IWebDriver driver)
    {
        try
        {
            driver.Navigate().Refresh();
        }
        catch (WebDriverException)
        {
            // Ignore refresh failures; the automation can continue without them.
        }
    }

    private sealed class NullTelemetry : ITelemetry
    {
        public static ITelemetry Instance { get; } = new NullTelemetry();

        public void Info(string message, Exception? exception = null)
        {
        }

        public void Warn(string message, Exception? exception = null)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
