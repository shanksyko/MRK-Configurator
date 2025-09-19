using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using OpenQA.Selenium;

namespace Mieruka.Automation.Login;

/// <summary>
/// Provides helpers to automate login flows using Selenium WebDriver.
/// </summary>
public sealed class LoginService
{
    private const int MinimumTimeoutSeconds = 1;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly string[] UsernameKeywords =
    {
        "user",
        "login",
        "email",
        "account",
        "name",
        "usuario",
        "utilizador",
    };

    private static readonly string[] PasswordKeywords =
    {
        "pass",
        "pwd",
        "senha",
        "password",
    };

    private static readonly string[] SubmitKeywords =
    {
        "login",
        "entrar",
        "sign",
        "submit",
        "acessar",
    };

    private readonly ITelemetry _telemetry;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginService"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to record automation events.</param>
    public LoginService(ITelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _telemetry = telemetry;
    }

    /// <summary>
    /// Attempts to perform an automated login using the provided profile.
    /// </summary>
    /// <param name="driver">Driver connected to the login page.</param>
    /// <param name="profile">Profile describing how the login should be performed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><c>true</c> when the automation was executed, otherwise <c>false</c>.</returns>
    public async Task<bool> TryLoginAsync(IWebDriver driver, LoginProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrEmpty(profile.Username) && string.IsNullOrEmpty(profile.Password) && string.IsNullOrWhiteSpace(profile.Script))
        {
            return false;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(profile.TimeoutSeconds, MinimumTimeoutSeconds));

        var userField = await ResolveFieldAsync(
            driver,
            profile.UserSelector,
            timeout,
            () => ResolveUsernameField(driver),
            cancellationToken);

        var automationPerformed = false;

        if (userField is null)
        {
            _telemetry.Warn("Login automation timed out while waiting for the username field.");
        }

        var passwordField = await ResolveFieldAsync(
            driver,
            profile.PassSelector,
            timeout,
            () => ResolvePasswordField(driver),
            cancellationToken);

        if (passwordField is null)
        {
            _telemetry.Warn("Login automation timed out while waiting for the password field.");
        }

        if (userField is not null && passwordField is not null)
        {
            var usernameApplied = await ApplyValueAsync(driver, profile.Username, userField, () => ResolveUsernameField(driver), timeout, cancellationToken);
            if (!usernameApplied)
            {
                _telemetry.Warn("Failed to populate the username field during login automation.");
            }
            else
            {
                var passwordApplied = await ApplyValueAsync(driver, profile.Password, passwordField, () => ResolvePasswordField(driver), timeout, cancellationToken);
                if (!passwordApplied)
                {
                    _telemetry.Warn("Failed to populate the password field during login automation.");
                }
                else
                {
                    automationPerformed = true;

                    var submitElement = await ResolveFieldAsync(
                        driver,
                        profile.SubmitSelector,
                        timeout,
                        () => ResolveSubmitElement(driver),
                        cancellationToken);

                    if (submitElement is not null)
                    {
                        if (!await ClickAsync(driver, submitElement, () => ResolveSubmitElement(driver), timeout, cancellationToken))
                        {
                            _telemetry.Warn("Failed to activate the submit element during login automation.");
                        }
                    }
                    else
                    {
                        try
                        {
                            passwordField.SendKeys(Keys.Enter);
                        }
                        catch (Exception exception) when (exception is InvalidElementStateException or StaleElementReferenceException)
                        {
                            _telemetry.Warn("Unable to submit the login form using the password field.", exception);
                        }
                    }
                }
            }
        }

        if (ExecuteCustomScript(driver, profile))
        {
            automationPerformed = true;
        }

        if (automationPerformed)
        {
            _telemetry.Info("Login automation executed using Selenium.");
        }

        return automationPerformed;
    }

    private async Task<IWebElement?> ResolveFieldAsync(
        IWebDriver driver,
        string? selector,
        TimeSpan timeout,
        Func<IWebElement?> heuristic,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(selector))
        {
            var located = await WaitForElementAsync(driver, () => FindBySelector(driver, selector), timeout, cancellationToken);
            if (located is not null)
            {
                return located;
            }

            _telemetry.Warn($"Timeout while waiting for selector '{selector}'.");
        }

        return await WaitForElementAsync(driver, heuristic, timeout, cancellationToken);
    }

    private async Task<bool> ApplyValueAsync(
        IWebDriver driver,
        string value,
        IWebElement element,
        Func<IWebElement?> resolver,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            element.Clear();
            element.SendKeys(value ?? string.Empty);
            return true;
        }
        catch (StaleElementReferenceException)
        {
            var refreshed = await WaitForElementAsync(driver, resolver, timeout, cancellationToken);
            if (refreshed is null)
            {
                return false;
            }

            refreshed.Clear();
            refreshed.SendKeys(value ?? string.Empty);
            return true;
        }
        catch (InvalidElementStateException exception)
        {
            _telemetry.Warn("Login automation could not fill a field because it is not interactable.", exception);
            return false;
        }
    }

    private async Task<bool> ClickAsync(
        IWebDriver driver,
        IWebElement element,
        Func<IWebElement?> resolver,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            element.Click();
            return true;
        }
        catch (StaleElementReferenceException)
        {
            var refreshed = await WaitForElementAsync(driver, resolver, timeout, cancellationToken);
            if (refreshed is null)
            {
                return false;
            }

            refreshed.Click();
            return true;
        }
        catch (ElementClickInterceptedException exception)
        {
            _telemetry.Warn("Login automation was blocked while clicking the submit element.", exception);
            return false;
        }
        catch (InvalidElementStateException exception)
        {
            _telemetry.Warn("Submit element is not interactable during login automation.", exception);
            return false;
        }
    }

    private async Task<IWebElement?> WaitForElementAsync(
        IWebDriver driver,
        Func<IWebElement?> resolver,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var element = resolver();
                if (IsInteractable(element))
                {
                    return element;
                }
            }
            catch (NoSuchElementException)
            {
                // Element not available yet, continue waiting.
            }
            catch (StaleElementReferenceException)
            {
                // DOM refreshed, retry until timeout.
            }
            catch (InvalidSelectorException exception)
            {
                _telemetry.Warn("Login automation received an invalid selector.", exception);
                return null;
            }
            catch (WebDriverException)
            {
                try
                {
                    if (!driver.WindowHandles.Any())
                    {
                        return null;
                    }
                }
                catch (WebDriverException)
                {
                    return null;
                }
            }

            var delay = deadline - DateTime.UtcNow;
            if (delay <= TimeSpan.Zero)
            {
                break;
            }

            var wait = delay < PollInterval ? delay : PollInterval;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }
        }

        return null;
    }

    private static IWebElement? FindBySelector(IWebDriver driver, string selector)
    {
        var by = BuildBy(selector);
        return driver.FindElements(by).FirstOrDefault(IsInteractable);
    }

    private static IWebElement? ResolveUsernameField(IWebDriver driver)
    {
        var candidates = driver.FindElements(By.CssSelector("input"));
        var withKeywords = candidates.FirstOrDefault(element =>
            IsInteractable(element) &&
            MatchesInputType(element, allowTextual: true) &&
            MatchesKeywords(element, UsernameKeywords));

        if (withKeywords is not null)
        {
            return withKeywords;
        }

        return candidates.FirstOrDefault(element =>
            IsInteractable(element) &&
            MatchesInputType(element, allowTextual: true));
    }

    private static IWebElement? ResolvePasswordField(IWebDriver driver)
    {
        var passwords = driver.FindElements(By.CssSelector("input"));
        return passwords.FirstOrDefault(element =>
            IsInteractable(element) &&
            MatchesPassword(element));
    }

    private static IWebElement? ResolveSubmitElement(IWebDriver driver)
    {
        var buttons = new List<IWebElement>();
        buttons.AddRange(driver.FindElements(By.CssSelector("button")));
        buttons.AddRange(driver.FindElements(By.CssSelector("input[type='submit'],input[type='button']")));

        return buttons.FirstOrDefault(element =>
            IsInteractable(element) &&
            MatchesKeywords(element, SubmitKeywords));
    }

    private bool ExecuteCustomScript(IWebDriver driver, LoginProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Script))
        {
            return false;
        }

        if (driver is not IJavaScriptExecutor executor)
        {
            _telemetry.Warn("Custom login script could not be executed because the driver does not support JavaScript execution.");
            return false;
        }

        try
        {
            executor.ExecuteScript(profile.Script, profile.Username, profile.Password);
            return true;
        }
        catch (Exception exception)
        {
            _telemetry.Warn("Custom login script failed during execution.", exception);
            return false;
        }
    }

    private static bool MatchesKeywords(IWebElement element, IReadOnlyCollection<string> keywords)
    {
        foreach (var attribute in GetRelevantAttributes(element))
        {
            if (ContainsKeyword(attribute, keywords))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesInputType(IWebElement element, bool allowTextual)
    {
        var type = (element.GetDomAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(type))
        {
            return allowTextual;
        }

        if (allowTextual && (type == "text" || type == "email"))
        {
            return true;
        }

        return allowTextual && type is "number" or "tel";
    }

    private static bool MatchesPassword(IWebElement element)
    {
        var type = (element.GetDomAttribute("type") ?? string.Empty).Trim().ToLowerInvariant();
        if (type == "password")
        {
            return true;
        }

        return ContainsKeyword(element.GetDomAttribute("name"), PasswordKeywords) ||
               ContainsKeyword(element.GetDomAttribute("id"), PasswordKeywords);
    }

    private static bool ContainsKeyword(string? value, IReadOnlyCollection<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToLower(CultureInfo.InvariantCulture);
        return keywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
    }

    private static IEnumerable<string?> GetRelevantAttributes(IWebElement element)
    {
        yield return element.GetDomAttribute("id");
        yield return element.GetDomAttribute("name");
        yield return element.GetDomAttribute("placeholder");
        yield return element.GetDomAttribute("aria-label");
        yield return element.GetDomAttribute("title");
    }

    private static bool IsInteractable(IWebElement? element)
    {
        return element is not null && element.Displayed && element.Enabled;
    }

    private static By BuildBy(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new ArgumentException("Selector cannot be empty.", nameof(selector));
        }

        var trimmed = selector.Trim();

        if (trimmed.StartsWith("css=", StringComparison.OrdinalIgnoreCase))
        {
            return By.CssSelector(trimmed[4..]);
        }

        if (trimmed.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
        {
            return By.XPath(trimmed[6..]);
        }

        if (trimmed.StartsWith("id=", StringComparison.OrdinalIgnoreCase))
        {
            return By.Id(trimmed[3..]);
        }

        if (trimmed.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
        {
            return By.Name(trimmed[5..]);
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("(//", StringComparison.Ordinal))
        {
            return By.XPath(trimmed);
        }

        return By.CssSelector(trimmed);
    }
}
