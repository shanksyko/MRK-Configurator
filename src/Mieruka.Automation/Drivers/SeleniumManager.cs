using System;
using System.Collections;
using System.Collections.Generic;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using OpenQA.Selenium;

namespace Mieruka.Automation.Drivers;

/// <summary>
/// Handles bootstrap operations for Selenium WebDriver instances.
/// </summary>
public sealed class SeleniumManager
{
    private const string BootstrapUrl = "about:blank";

    private readonly BrowserLauncher _browserLauncher;
    private readonly ITelemetry _telemetry;
    private readonly BrowserArgumentsSettings? _globalArguments;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeleniumManager"/> class.
    /// </summary>
    /// <param name="browserLauncher">Launcher used to create <see cref="IWebDriver"/> instances.</param>
    /// <param name="telemetry">Telemetry sink used to record bootstrap details.</param>
    /// <param name="globalArguments">Optional global browser arguments applied during bootstrap.</param>
    public SeleniumManager(BrowserLauncher browserLauncher, ITelemetry telemetry, BrowserArgumentsSettings? globalArguments = null)
    {
        ArgumentNullException.ThrowIfNull(browserLauncher);
        ArgumentNullException.ThrowIfNull(telemetry);

        _browserLauncher = browserLauncher;
        _telemetry = telemetry;
        _globalArguments = globalArguments;
    }

    /// <summary>
    /// Bootstraps Selenium WebDriver for the specified browsers.
    /// </summary>
    /// <param name="browsers">Collection of browsers that should be validated.</param>
    public void Bootstrap(IEnumerable<BrowserType> browsers)
    {
        ArgumentNullException.ThrowIfNull(browsers);

        foreach (var browser in browsers)
        {
            InitializeBrowser(browser);
        }
    }

    /// <summary>
    /// Bootstraps Selenium WebDriver for all supported browsers.
    /// </summary>
    public void Bootstrap()
    {
        Bootstrap(new[] { BrowserType.Chrome, BrowserType.Edge });
    }

    private void InitializeBrowser(BrowserType browser)
    {
        try
        {
            using var driver = CreateDriver(browser);
            driver.Navigate().GoToUrl(BootstrapUrl);

            ValidateVersion(driver, browser);

            _telemetry.Info($"Selenium bootstrap successful for {browser}.");
        }
        catch (Exception exception)
        {
            _telemetry.Error($"Failed to bootstrap Selenium for {browser}.", exception);
        }
    }

    private IWebDriver CreateDriver(BrowserType browser)
    {
        var siteConfig = new SiteConfig
        {
            Browser = browser,
            Url = BootstrapUrl,
        };

        return _browserLauncher.Launch(siteConfig, _globalArguments);
    }

    private void ValidateVersion(IWebDriver driver, BrowserType browser)
    {
        if (driver is not IHasCapabilities { Capabilities: not null } hasCapabilities)
        {
            return;
        }

        var capabilities = hasCapabilities.Capabilities;
        var browserVersion = ParseVersion(capabilities.GetCapability("browserVersion")?.ToString());
        var driverVersion = ParseVersion(GetDriverVersion(capabilities, browser));

        if (browserVersion is null || driverVersion is null)
        {
            return;
        }

        if (!browserVersion.Equals(driverVersion))
        {
            _telemetry.Warn($"Detected {browser} browser version {browserVersion} and driver version {driverVersion}. Versions should match to avoid runtime issues.");
        }
    }

    private static string? GetDriverVersion(ICapabilities capabilities, BrowserType browser)
    {
        var (capabilityKey, versionKey) = browser switch
        {
            BrowserType.Chrome => ("chrome", "chromedriverVersion"),
            BrowserType.Edge => ("msedge", "msedgedriverVersion"),
            _ => (null, null),
        };

        if (capabilityKey is null || versionKey is null)
        {
            return null;
        }

        var rawCapability = capabilities.GetCapability(capabilityKey);

        if (rawCapability is IDictionary dictionary && dictionary.Contains(versionKey) && dictionary[versionKey] is { } value)
        {
            return value.ToString();
        }

        if (rawCapability is IReadOnlyDictionary<string, object?> readOnlyDictionary && readOnlyDictionary.TryGetValue(versionKey, out var readOnlyValue))
        {
            return readOnlyValue?.ToString();
        }

        var directValue = capabilities.GetCapability(versionKey);
        return directValue?.ToString();
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var length = 0;

        while (length < trimmed.Length && (char.IsDigit(trimmed[length]) || trimmed[length] == '.'))
        {
            length++;
        }

        if (length == 0)
        {
            return null;
        }

        var versionText = trimmed[..length];
        return Version.TryParse(versionText, out var parsed) ? parsed : null;
    }
}
