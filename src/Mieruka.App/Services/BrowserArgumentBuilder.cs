using System;
using System.Collections.Generic;
using Mieruka.Core.Models;

namespace Mieruka.App.Services;

/// <summary>
/// Centralised logic for resolving browser executables, formatting command-line
/// arguments and building the full argument string for a <see cref="SiteConfig"/>.
/// <para>
/// This class replaces the duplicated implementations that previously lived in
/// <c>WatchdogService</c>, <c>SiteTestService</c> and <c>SiteEditorDialog</c>.
/// </para>
/// </summary>
internal static class BrowserArgumentBuilder
{
    // ── Executable resolution ─────────────────────────────────────────────

    /// <summary>
    /// Returns the conventional executable name for the given browser on the current platform.
    /// </summary>
    public static string ResolveBrowserExecutable(BrowserType browser)
    {
        if (OperatingSystem.IsWindows())
        {
            return browser switch
            {
                BrowserType.Chrome => "chrome.exe",
                BrowserType.Edge => "msedge.exe",
                BrowserType.Firefox => "firefox.exe",
                BrowserType.Brave => "brave.exe",
                _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
            };
        }

        return browser switch
        {
            BrowserType.Chrome => "google-chrome",
            BrowserType.Edge => "microsoft-edge",
            BrowserType.Firefox => "firefox",
            BrowserType.Brave => "brave-browser",
            _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
        };
    }

    // ── Argument formatting ───────────────────────────────────────────────

    /// <summary>
    /// Formats a named argument with a value, escaping embedded double-quotes.
    /// Produces <c>--name="value"</c>.
    /// </summary>
    public static string FormatArgument(string name, string value)
    {
        var sanitized = value.Replace("\"", "\\\"");
        return $"{name}=\"{sanitized}\"";
    }

    /// <summary>
    /// Returns <see langword="true"/> when the argument list already contains
    /// a matching entry (exact or prefix match).
    /// </summary>
    public static bool ContainsArgument(IEnumerable<string> arguments, string name, bool matchByPrefix = false)
    {
        foreach (var argument in arguments)
        {
            if (matchByPrefix)
            {
                if (argument.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ── Full argument building ────────────────────────────────────────────

    /// <summary>
    /// Collects all browser arguments for the given site including global settings,
    /// per-site arguments, user-data-dir, profile-directory, kiosk and app-mode flags.
    /// </summary>
    public static List<string> CollectBrowserArguments(
        SiteConfig site,
        BrowserArgumentsSettings? globalSettings = null)
    {
        ArgumentNullException.ThrowIfNull(site);

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

        // Global per-browser arguments.
        if (globalSettings is not null)
        {
            foreach (var argument in globalSettings.ForBrowser(site.Browser))
            {
                AddArgument(argument);
            }
        }

        // Per-site custom arguments.
        foreach (var argument in site.BrowserArguments ?? Array.Empty<string>())
        {
            AddArgument(argument);
        }

        // User data directory.
        if (!string.IsNullOrWhiteSpace(site.UserDataDirectory)
            && !ContainsArgument(arguments, "--user-data-dir", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--user-data-dir", site.UserDataDirectory));
        }

        // Profile directory.
        if (!string.IsNullOrWhiteSpace(site.ProfileDirectory)
            && !ContainsArgument(arguments, "--profile-directory", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--profile-directory", site.ProfileDirectory));
        }

        // Kiosk mode.
        if (site.KioskMode && !ContainsArgument(arguments, "--kiosk"))
        {
            AddArgument("--kiosk");
        }

        // App mode.
        if (site.AppMode
            && !string.IsNullOrWhiteSpace(site.Url)
            && !ContainsArgument(arguments, "--app", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--app", site.Url));
        }

        return arguments;
    }

    /// <summary>
    /// Builds the final argument string for Process.Start, including the trailing URL when not in app mode.
    /// </summary>
    public static string BuildBrowserArgumentString(
        SiteConfig site,
        BrowserArgumentsSettings? globalSettings = null)
    {
        var arguments = CollectBrowserArguments(site, globalSettings);

        if (!site.AppMode && !string.IsNullOrWhiteSpace(site.Url))
        {
            arguments.Add(site.Url);
        }

        return string.Join(' ', arguments);
    }
}
