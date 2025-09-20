using System;
using System.Collections.Generic;
using System.Linq;
using Mieruka.Core.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Chromium;

namespace Mieruka.App.Services;

internal static class WebDriverFactory
{
    public static IWebDriver Create(SiteConfig site, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(arguments);

        var sanitizedArguments = arguments
            .Where(static argument => !string.IsNullOrWhiteSpace(argument))
            .ToList();

        try
        {
            return site.Browser switch
            {
                BrowserType.Chrome => CreateChromeDriver(site, sanitizedArguments),
                BrowserType.Edge => CreateEdgeDriver(site, sanitizedArguments),
                _ => throw new NotSupportedException($"Browser '{site.Browser}' is not supported for Selenium tests."),
            };
        }
        catch (DriverServiceNotFoundException ex)
        {
            throw BuildDriverMissingException(site.Browser, ex);
        }
        catch (WebDriverException ex) when (ex.InnerException is DriverServiceNotFoundException driverException)
        {
            throw BuildDriverMissingException(site.Browser, driverException);
        }
        catch (Exception ex) when (ex is not NotSupportedException)
        {
            throw new NotSupportedException(BuildGeneralDriverError(site.Browser), ex);
        }
    }

    private static IWebDriver CreateChromeDriver(SiteConfig site, IReadOnlyCollection<string> arguments)
    {
        var options = new ChromeOptions();
        ApplyArguments(options, arguments);

        var driver = new ChromeDriver(options);
        NavigateIfNeeded(driver, site);
        return driver;
    }

    private static IWebDriver CreateEdgeDriver(SiteConfig site, IReadOnlyCollection<string> arguments)
    {
        var options = new EdgeOptions();
        options.AddAdditionalOption("useChromium", true);
        ApplyArguments(options, arguments);

        var driver = new EdgeDriver(options);
        NavigateIfNeeded(driver, site);
        return driver;
    }

    private static void ApplyArguments(ChromiumOptions options, IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            options.AddArgument(argument);
        }
    }

    private static void NavigateIfNeeded(IWebDriver driver, SiteConfig site)
    {
        if (site.AppMode || string.IsNullOrWhiteSpace(site.Url))
        {
            return;
        }

        driver.Navigate().GoToUrl(site.Url);
    }

    private static NotSupportedException BuildDriverMissingException(BrowserType browser, Exception inner)
    {
        var driverName = browser switch
        {
            BrowserType.Chrome => "ChromeDriver",
            BrowserType.Edge => "EdgeDriver",
            _ => "navegador",
        };

        var message =
            $"Não foi possível localizar o driver necessário para '{browser}'. Instale o {driverName} e garanta que o executável esteja no PATH ou configurado conforme a documentação do MRK Configurator.";
        return new NotSupportedException(message, inner);
    }

    private static string BuildGeneralDriverError(BrowserType browser)
    {
        return $"Falha ao inicializar o Selenium para o navegador '{browser}'. Verifique se o driver correspondente está instalado e acessível.";
    }
}
