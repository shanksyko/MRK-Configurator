using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Security;
using Mieruka.Core.Services;
using OpenQA.Selenium;

namespace Mieruka.Automation.Tabs;

/// <summary>
/// Monitors browser windows and enforces a whitelist for allowed tab hosts.
/// </summary>
public sealed class TabManager
{
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ITelemetry _telemetry;
    private List<string>? _staleKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="TabManager"/> class.
    /// </summary>
    /// <param name="telemetry">Telemetry sink used to record automation events.</param>
    public TabManager(ITelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _telemetry = telemetry;
    }

    /// <summary>
    /// Continuously enforces the tab whitelist for the provided driver.
    /// </summary>
    /// <param name="driver">Driver whose windows should be monitored.</param>
    /// <param name="allowedTabHosts">Collection of host names allowed to remain open.</param>
    /// <param name="cancellationToken">Token used to cancel the monitoring loop.</param>
    public async Task MonitorAsync(
        IWebDriver driver,
        IEnumerable<string> allowedTabHosts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(allowedTabHosts);

        var whitelist = new HashSet<string>(
            allowedTabHosts
                .Where(static host => !string.IsNullOrWhiteSpace(host))
                .Select(host => TryNormalizeWhitelistHost(host!))
                .Where(static host => host is not null)
                .Select(static host => host!),
            StringComparer.OrdinalIgnoreCase);

        var observations = new Dictionary<string, WindowObservation>();

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyCollection<string> handles;
            try
            {
                handles = driver.WindowHandles;
            }
            catch (WebDriverException exception)
            {
                _telemetry.Warn("Unable to inspect browser windows while enforcing the tab whitelist.", exception);
                break;
            }

            var now = DateTime.UtcNow;
            foreach (var handle in handles)
            {
                if (!observations.ContainsKey(handle))
                {
                    observations[handle] = new WindowObservation(now);
                }
            }

            foreach (var known in observations.Keys)
            {
                if (!handles.Contains(known))
                {
                    _staleKeys ??= new List<string>();
                    _staleKeys.Add(known);
                }
            }

            if (_staleKeys is { Count: > 0 })
            {
                foreach (var key in _staleKeys)
                {
                    observations.Remove(key);
                }

                _staleKeys.Clear();
            }

            foreach (var handle in handles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!observations.TryGetValue(handle, out var observation))
                {
                    observation = new WindowObservation(now);
                    observations[handle] = observation;
                }

                var host = ResolveHost(driver, handle);
                var normalizedHost = NormalizeObservedHost(host);

                if (normalizedHost is not null && whitelist.Contains(normalizedHost))
                {
                    observation.StateSince = now;
                    observation.LastHost = normalizedHost;
                    continue;
                }

                if (!HostsEqual(observation.LastHost, normalizedHost))
                {
                    observation.StateSince = now;
                    observation.LastHost = normalizedHost;
                }

                if (now - observation.StateSince < CloseDelay)
                {
                    continue;
                }

                CloseWindow(driver, handle, normalizedHost ?? host);
                observations.Remove(handle);
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private string? ResolveHost(IWebDriver driver, string handle)
    {
        string? previousHandle = TryGetCurrentWindowHandle(driver);

        try
        {
            driver.SwitchTo().Window(handle);
            return ExtractHost(driver.Url);
        }
        catch (NoSuchWindowException)
        {
            return null;
        }
        catch (WebDriverException exception)
        {
            _telemetry.Warn($"Unable to determine the host for window '{handle}'.", exception);
            return null;
        }
        finally
        {
            if (previousHandle is not null && !string.Equals(previousHandle, handle, StringComparison.Ordinal))
            {
                TrySwitchTo(driver, previousHandle);
            }
        }
    }

    private void CloseWindow(IWebDriver driver, string handle, string? host)
    {
        try
        {
            driver.SwitchTo().Window(handle);
        }
        catch (NoSuchWindowException)
        {
            return;
        }
        catch (WebDriverException exception)
        {
            _telemetry.Warn($"Unable to switch to blocked window '{handle}'.", exception);
            return;
        }

        try
        {
            driver.Close();
            _telemetry.Info($"Closed browser tab for blocked host '{host ?? "unknown"}'.");
        }
        catch (WebDriverException exception)
        {
            _telemetry.Warn($"Failed to close browser tab for blocked host '{host ?? "unknown"}'.", exception);
        }
        finally
        {
            TrySwitchToFallbackWindow(driver);
        }
    }

    private static string? ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return string.IsNullOrEmpty(uri.Host) ? null : uri.IdnHost;
    }

    private static string? NormalizeObservedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        try
        {
            return InputSanitizer.SanitizeHost(host);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string? TryNormalizeWhitelistHost(string host)
    {
        var trimmed = host.Trim();

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            var tail = trimmed[2..];
            try
            {
                var normalizedTail = InputSanitizer.SanitizeHost(tail);
                return string.IsNullOrEmpty(normalizedTail) ? null : "*." + normalizedTail;
            }
            catch (Exception)
            {
                _telemetry.Warn("Entrada de whitelist rejeitada por conter host inválido. Valor descartado.");
                return null;
            }
        }

        try
        {
            var normalized = InputSanitizer.SanitizeHost(trimmed);
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }
        catch (Exception)
        {
            _telemetry.Warn("Entrada de whitelist rejeitada por conter host inválido. Valor descartado.");
            return null;
        }
    }

    private static bool HostsEqual(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetCurrentWindowHandle(IWebDriver driver)
    {
        try
        {
            return driver.CurrentWindowHandle;
        }
        catch (NoSuchWindowException)
        {
            return null;
        }
        catch (WebDriverException)
        {
            return null;
        }
    }

    private void TrySwitchTo(IWebDriver driver, string handle)
    {
        try
        {
            driver.SwitchTo().Window(handle);
        }
        catch (NoSuchWindowException)
        {
            TrySwitchToFallbackWindow(driver);
        }
        catch (WebDriverException)
        {
            TrySwitchToFallbackWindow(driver);
        }
    }

    private void TrySwitchToFallbackWindow(IWebDriver driver)
    {
        try
        {
            var handles = driver.WindowHandles;
            var target = handles.FirstOrDefault();
            if (target is not null)
            {
                driver.SwitchTo().Window(target);
            }
        }
        catch (WebDriverException)
        {
            // Intentionally ignored.
        }
    }

    private sealed class WindowObservation
    {
        public WindowObservation(DateTime stateSince)
        {
            StateSince = stateSince;
        }

        public DateTime StateSince { get; set; }

        public string? LastHost { get; set; }
    }
}
