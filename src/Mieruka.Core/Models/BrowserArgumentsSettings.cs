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
    public IList<string> Chrome { get; init; } = new List<string>();

    /// <summary>
    /// Global arguments applied to Microsoft Edge instances.
    /// </summary>
    public IList<string> Edge { get; init; } = new List<string>();
}
