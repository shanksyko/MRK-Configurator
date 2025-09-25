using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Mieruka.Core.Services;
using OpenQA.Selenium;
using NetCookie = System.Net.Cookie;
using SelCookie = OpenQA.Selenium.Cookie;

namespace Mieruka.Automation.Login;

/// <summary>
/// Bridges Selenium cookies with the encrypted cookie store used by the player.
/// </summary>
public sealed class CookieBridge
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(12);

    private readonly CookieSafeStore _cookieStore;
    private readonly ITelemetry _telemetry;
    private readonly UrlAllowlist? _allowlist;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="CookieBridge"/> class.
    /// </summary>
    /// <param name="cookieStore">Cookie store used to persist cookies.</param>
    /// <param name="telemetry">Telemetry sink used to report issues.</param>
    /// <param name="allowlist">Optional allowlist used to validate cookie hosts.</param>
    /// <param name="ttl">Optional time-to-live applied to persisted cookies.</param>
    public CookieBridge(
        CookieSafeStore cookieStore,
        ITelemetry telemetry,
        UrlAllowlist? allowlist = null,
        TimeSpan? ttl = null)
    {
        _cookieStore = cookieStore ?? throw new ArgumentNullException(nameof(cookieStore));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _allowlist = allowlist;
        _ttl = ttl is { } value && value > TimeSpan.Zero ? value : DefaultTtl;
    }

    /// <summary>
    /// Restores cookies from the secure store into the Selenium driver.
    /// </summary>
    /// <param name="driver">Driver that should receive the cookies.</param>
    /// <param name="site">Site configuration associated with the cookies.</param>
    /// <param name="targetUri">Target URI used to scope the cookies.</param>
    public bool RestoreCookies(IWebDriver driver, SiteConfig site, Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(targetUri);

        if (!IsAllowed(targetUri, site.Id))
        {
            _telemetry.Warn($"Cookie restoration blocked by allowlist for '{targetUri}'.");
            return false;
        }

        if (!_cookieStore.TryGet(targetUri.Host, out var cookies))
        {
            return false;
        }

        var restored = false;
        foreach (var cookie in cookies)
        {
            if (cookie is null)
            {
                continue;
            }

            try
            {
                var domain = string.IsNullOrEmpty(cookie.Domain) ? targetUri.Host : cookie.Domain;
                var expires = cookie.Expires == DateTime.MinValue ? (DateTime?)null : cookie.Expires;
                var seleniumCookie = new SelCookie(
                    cookie.Name,
                    cookie.Value,
                    domain,
                    cookie.Path,
                    expires);

                driver.Manage().Cookies.AddCookie(seleniumCookie);
                restored = true;
            }
            catch (Exception exception) when (exception is WebDriverException or CookieException)
            {
                _telemetry.Warn($"Failed to restore cookie '{cookie.Name}' for '{targetUri.Host}'.", exception);
            }
        }

        return restored;
    }

    /// <summary>
    /// Persists cookies from the Selenium driver into the secure store.
    /// </summary>
    /// <param name="driver">Driver that currently holds the cookies.</param>
    /// <param name="site">Site configuration associated with the cookies.</param>
    /// <param name="targetUri">Target URI used to scope the cookies.</param>
    public bool PersistCookies(IWebDriver driver, SiteConfig site, Uri targetUri)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(targetUri);

        if (!IsAllowed(targetUri, site.Id))
        {
            _telemetry.Warn($"Cookie persistence blocked by allowlist for '{targetUri}'.");
            return false;
        }

        IReadOnlyCollection<SelCookie> cookies;
        try
        {
            cookies = driver.Manage().Cookies.AllCookies;
        }
        catch (WebDriverException exception)
        {
            _telemetry.Warn($"Unable to enumerate cookies for '{targetUri.Host}'.", exception);
            return false;
        }

        if (cookies.Count == 0)
        {
            return false;
        }

        var relevant = cookies
            .Where(cookie => cookie is not null)
            .Where(cookie => BelongsToHost(cookie!, targetUri.Host))
            .Select(cookie => ConvertCookie(cookie!, targetUri.Host))
            .Where(cookie => cookie is not null)
            .Select(cookie => cookie!)
            .ToList();

        if (relevant.Count == 0)
        {
            return false;
        }

        if (!_cookieStore.Put(targetUri.Host, relevant, _ttl))
        {
            _telemetry.Warn($"Cookie store rejected cookies for '{targetUri.Host}'.");
            return false;
        }

        return true;
    }

    private static bool BelongsToHost(SelCookie cookie, string host)
    {
        if (string.IsNullOrEmpty(cookie.Domain))
        {
            return true;
        }

        if (string.Equals(cookie.Domain, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (cookie.Domain.StartsWith(".", StringComparison.Ordinal) &&
            host.EndsWith(cookie.Domain[1..], StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static NetCookie? ConvertCookie(SelCookie cookie, string host)
    {
        try
        {
            var domain = string.IsNullOrEmpty(cookie.Domain) ? host : cookie.Domain;
            var netCookie = new NetCookie(cookie.Name, cookie.Value, cookie.Path ?? "/", domain);

            if (cookie.Expiry is { } expires)
            {
                netCookie.Expires = expires;
            }

            netCookie.HttpOnly = cookie.IsHttpOnly;
            netCookie.Secure = cookie.Secure;
            return netCookie;
        }
        catch (CookieException)
        {
            return null;
        }
    }

    private bool IsAllowed(Uri uri, string siteId)
    {
        if (_allowlist is null)
        {
            return true;
        }

        return _allowlist.IsAllowed(uri, siteId);
    }
}
