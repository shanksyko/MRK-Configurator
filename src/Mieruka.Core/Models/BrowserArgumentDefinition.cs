using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Category used to group browser arguments in the UI.
/// </summary>
public enum BrowserArgumentCategory
{
    /// <summary>Display and window mode arguments.</summary>
    Display,

    /// <summary>Privacy and incognito related arguments.</summary>
    Privacy,

    /// <summary>Security related arguments.</summary>
    Security,

    /// <summary>Performance and resource arguments.</summary>
    Performance,

    /// <summary>Network and proxy arguments.</summary>
    Network,

    /// <summary>Content and feature toggle arguments.</summary>
    Content,

    /// <summary>Debug and development arguments.</summary>
    Debug,
}

/// <summary>
/// Describes a single browser startup argument that can be selected by the user.
/// </summary>
/// <param name="Flag">The command-line flag (e.g. "--kiosk").</param>
/// <param name="DisplayName">Human-readable label for the UI.</param>
/// <param name="Description">Tooltip description of what the argument does.</param>
/// <param name="Category">Grouping category for the UI.</param>
/// <param name="ApplicableBrowsers">Browsers that support this argument. Empty means all browsers.</param>
/// <param name="RequiresValue">Whether the argument takes a value (e.g. --proxy-server=...).</param>
/// <param name="ValueHint">Placeholder hint when a value is required (e.g. "host:port").</param>
public sealed record class BrowserArgumentDefinition(
    string Flag,
    string DisplayName,
    string Description,
    BrowserArgumentCategory Category,
    IReadOnlyList<BrowserType> ApplicableBrowsers,
    bool RequiresValue = false,
    string? ValueHint = null)
{
    /// <summary>
    /// Returns <see langword="true"/> when this argument is valid for the given browser.
    /// </summary>
    public bool IsApplicableTo(BrowserType browser)
        => ApplicableBrowsers.Count == 0 || ApplicableBrowsers.Contains(browser);

    /// <summary>
    /// Builds the full command-line string for this argument.
    /// When <see cref="RequiresValue"/> is <see langword="true"/> and a value is provided,
    /// the result is <c>--flag=value</c>; otherwise just the flag.
    /// </summary>
    public string BuildArgument(string? value = null)
    {
        if (RequiresValue && !string.IsNullOrWhiteSpace(value))
        {
            return $"{Flag}={value}";
        }

        return Flag;
    }
}
