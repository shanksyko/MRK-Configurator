using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Configuration settings for a website rendered by the player.
/// </summary>
public sealed record class SiteConfig
{
    /// <summary>
    /// Unique identifier for the site entry.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Address of the site that should be opened.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Preferred browser for rendering the site.
    /// </summary>
    public BrowserType Browser { get; init; } = BrowserType.Chrome;

    /// <summary>
    /// Additional arguments applied when launching the browser for this site.
    /// </summary>
    public IList<string> BrowserArguments { get; init; } = new List<string>();

    /// <summary>
    /// Optional user data directory used when launching Chromium browsers.
    /// </summary>
    public string? UserDataDirectory { get; init; }

    /// <summary>
    /// Optional profile directory used when launching Chromium browsers.
    /// </summary>
    public string? ProfileDirectory { get; init; }

    /// <summary>
    /// Launches the browser in app mode (chromeless window).
    /// </summary>
    public bool AppMode { get; init; }

    /// <summary>
    /// Controls whether the browser should use kiosk mode.
    /// </summary>
    public bool KioskMode { get; init; }

    /// <summary>
    /// Forces a refresh every time the site becomes active in the cycle.
    /// </summary>
    public bool ReloadOnActivate { get; init; }

    /// <summary>
    /// Optional interval for background reloads in seconds.
    /// </summary>
    public int? ReloadIntervalSeconds { get; init; }

    /// <summary>
    /// Custom headers applied when requesting the site.
    /// </summary>
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Watchdog related settings that describe how the site should be supervised.
    /// </summary>
    public WatchdogSettings Watchdog { get; init; } = new();

    /// <summary>
    /// Host names allowed to remain open when additional tabs or popups are created.
    /// </summary>
    public IList<string> AllowedTabHosts { get; init; } = new List<string>();

    /// <summary>
    /// Optional automation profile used to authenticate against the site.
    /// </summary>
    public LoginProfile? Login { get; init; }

    /// <summary>
    /// Desired window placement.
    /// </summary>
    public WindowConfig Window { get; init; } = new();

    /// <summary>
    /// Stable identifier of the monitor that should host the site window.
    /// </summary>
    public string TargetMonitorStableId { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the zone preset applied when positioning the site window.
    /// </summary>
    public string? TargetZonePresetId { get; init; }
}
