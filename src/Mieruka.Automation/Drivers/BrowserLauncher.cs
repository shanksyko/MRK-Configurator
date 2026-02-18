using System;
using System.Collections.Generic;
using System.Linq;
using Mieruka.Core.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Chromium;

namespace Mieruka.Automation.Drivers;

/// <summary>
/// Provides helpers for launching Chromium-based browsers with custom arguments.
/// </summary>
public sealed class BrowserLauncher
{
    private readonly WebDriverFactory _webDriverFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrowserLauncher"/> class.
    /// </summary>
    /// <param name="webDriverFactory">Factory responsible for creating <see cref="IWebDriver"/> instances.</param>
    public BrowserLauncher(WebDriverFactory webDriverFactory)
    {
        ArgumentNullException.ThrowIfNull(webDriverFactory);
        _webDriverFactory = webDriverFactory;
    }

    /// <summary>
    /// Launches a browser based on the provided configuration.
    /// </summary>
    /// <param name="site">Site configuration used to customize the launch.</param>
    /// <param name="globalArguments">Optional global browser arguments per browser type.</param>
    /// <returns>A configured <see cref="IWebDriver"/> instance.</returns>
    public IWebDriver Launch(SiteConfig site, BrowserArgumentsSettings? globalArguments = null)
    {
        ArgumentNullException.ThrowIfNull(site);

        var driver = site.Browser switch
        {
            BrowserType.Chrome => _webDriverFactory.Create(() => CreateChromeDriver(site, globalArguments)),
            BrowserType.Edge => _webDriverFactory.Create(() => CreateEdgeDriver(site, globalArguments)),
            _ => throw new NotSupportedException($"Browser '{site.Browser}' is not supported."),
        };

        if (!site.AppMode && !string.IsNullOrWhiteSpace(site.Url))
        {
            driver.Navigate().GoToUrl(site.Url);
        }

        return driver;
    }

    private IWebDriver CreateChromeDriver(SiteConfig site, BrowserArgumentsSettings? globalArguments)
    {
        var options = new ChromeOptions();
        ConfigureChromiumOptions(options, site, globalArguments);

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

    private IWebDriver CreateEdgeDriver(SiteConfig site, BrowserArgumentsSettings? globalArguments)
    {
        var options = new EdgeOptions();
        options.AddAdditionalOption("useChromium", true);
        ConfigureChromiumOptions(options, site, globalArguments);

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

    private static void ConfigureChromiumOptions(ChromiumOptions options, SiteConfig site, BrowserArgumentsSettings? globalArguments)
    {
        foreach (var argument in CollectArguments(site, globalArguments))
        {
            options.AddArgument(argument);
        }
    }

    private static IReadOnlyCollection<string> CollectArguments(SiteConfig site, BrowserArgumentsSettings? globalArguments)
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

        foreach (var argument in GetGlobalArguments(site.Browser, globalArguments))
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
            if (string.IsNullOrWhiteSpace(site.Url))
            {
                throw new InvalidOperationException($"Site '{site.Id}' requires a URL to use app mode.");
            }

            if (!ContainsArgument(arguments, "--app", matchByPrefix: true))
            {
                AddArgument(FormatArgument("--app", site.Url));
            }
        }

        return arguments;
    }

    private static IEnumerable<string> GetGlobalArguments(BrowserType browser, BrowserArgumentsSettings? globalArguments)
    {
        return browser switch
        {
            BrowserType.Chrome => globalArguments?.Chrome ?? Array.Empty<string>(),
            BrowserType.Edge => globalArguments?.Edge ?? Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
    }

    private static bool ContainsArgument(IEnumerable<string> arguments, string name, bool matchByPrefix = false)
    {
        return arguments.Any(argument =>
            matchByPrefix ? argument.StartsWith(name, StringComparison.OrdinalIgnoreCase) :
            string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatArgument(string name, string value)
    {
        var sanitized = value.Replace("\"", "\\\"");
        return $"{name}=\"{sanitized}\"";
    }
}
