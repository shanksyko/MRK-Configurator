using System;
using System.Collections.Generic;

namespace Mieruka.Core.Models;

/// <summary>
/// Defines reusable browser arguments applied globally per browser type.
/// </summary>
public sealed record class BrowserArgumentsSettings
{
    /// <summary>
    /// Global arguments applied to Google Chrome instances.
    /// </summary>
    public IReadOnlyList<string> Chrome { get; init; } = [];

    /// <summary>
    /// Global arguments applied to Microsoft Edge instances.
    /// </summary>
    public IReadOnlyList<string> Edge { get; init; } = [];

    /// <summary>
    /// Global arguments applied to Mozilla Firefox instances.
    /// </summary>
    public IReadOnlyList<string> Firefox { get; init; } = [];

    /// <summary>
    /// Global arguments applied to Brave Browser instances.
    /// </summary>
    public IReadOnlyList<string> Brave { get; init; } = [];

    /// <summary>
    /// Returns the global arguments for the specified browser type.
    /// </summary>
    public IReadOnlyList<string> ForBrowser(BrowserType browser)
    {
        return browser switch
        {
            BrowserType.Chrome => Chrome,
            BrowserType.Edge => Edge,
            BrowserType.Firefox => Firefox,
            BrowserType.Brave => Brave,
            _ => Array.Empty<string>(),
        };
    }
}
