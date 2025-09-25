using System;
using System.Collections.Generic;
using System.Globalization;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Mieruka.Core.Services;
using OpenQA.Selenium;

namespace Mieruka.Automation.Login;

/// <summary>
/// Validates whether a Selenium session represents a successful login.
/// </summary>
public sealed class SessionVerifier
{
    private readonly ITelemetry _telemetry;
    private readonly UrlAllowlist? _allowlist;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionVerifier"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to report verification status.</param>
    /// <param name="allowlist">Optional allowlist used to validate navigation targets.</param>
    public SessionVerifier(ITelemetry telemetry, UrlAllowlist? allowlist = null)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _allowlist = allowlist;
    }

    /// <summary>
    /// Checks if the driver appears to be logged in for the supplied site.
    /// </summary>
    /// <param name="driver">Driver whose state should be inspected.</param>
    /// <param name="site">Site configuration associated with the session.</param>
    /// <param name="targetUri">Primary URI for the site.</param>
    /// <returns><c>true</c> when the session looks authenticated.</returns>
    public bool Verify(IWebDriver driver, SiteConfig site, Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(targetUri);

        if (!TryGetCurrentUri(driver, out var currentUri))
        {
            _telemetry.Warn($"Unable to determine the current URL for site '{site.Id}'.");
            return false;
        }

        if (_allowlist is not null && !_allowlist.IsAllowed(currentUri, site.Id))
        {
            _telemetry.Warn($"Navigation to '{currentUri}' is not allowed for site '{site.Id}'.");
            return false;
        }

        if (!IsHostAllowed(currentUri, targetUri, site.AllowedTabHosts))
        {
            _telemetry.Warn($"Detected host '{currentUri.Host}' outside the configured allowlist for site '{site.Id}'.");
            return false;
        }

        if (!HasSessionCookies(driver))
        {
            _telemetry.Warn($"No session cookies detected for site '{site.Id}'.");
            return false;
        }

        if (ContainsSsoHints(driver, site.Login))
        {
            _telemetry.Warn($"Single sign-on prompt detected while verifying session for '{site.Id}'.");
            return false;
        }

        return true;
    }

    private static bool TryGetCurrentUri(IWebDriver driver, out Uri uri)
    {
        uri = default!;

        string? url;
        try
        {
            url = driver.Url;
        }
        catch (WebDriverException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return false;
        }

        return true;
    }

    private static bool IsHostAllowed(Uri current, Uri target, IEnumerable<string>? allowedHosts)
    {
        if (string.Equals(current.Host, target.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (allowedHosts is null)
        {
            return false;
        }

        foreach (var host in allowedHosts)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }

            string sanitized;
            try
            {
                sanitized = InputSanitizer.SanitizeHost(host);
            }
            catch (Exception)
            {
                continue;
            }

            if (string.Equals(sanitized, current.Host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (sanitized.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = sanitized[2..];
                if (current.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSessionCookies(IWebDriver driver)
    {
        try
        {
            return driver.Manage().Cookies.AllCookies.Count > 0;
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static bool ContainsSsoHints(IWebDriver driver, LoginProfile? profile)
    {
        if (profile?.SsoHints is not { Count: > 0 })
        {
            return false;
        }

        string source;
        try
        {
            source = driver.PageSource ?? string.Empty;
        }
        catch (WebDriverException)
        {
            return false;
        }

        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        var normalizedSource = source.ToLower(CultureInfo.InvariantCulture);
        foreach (var hint in profile.SsoHints)
        {
            if (string.IsNullOrWhiteSpace(hint))
            {
                continue;
            }

            var normalizedHint = hint.ToLower(CultureInfo.InvariantCulture);
            if (normalizedSource.Contains(normalizedHint))
            {
                return true;
            }
        }

        return false;
    }
}
