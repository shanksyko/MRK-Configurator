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
    /// Desired window placement.
    /// </summary>
    public WindowConfig Window { get; init; } = new();
}
