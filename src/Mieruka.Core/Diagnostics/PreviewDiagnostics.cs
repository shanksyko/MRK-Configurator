using System;

namespace Mieruka.Core.Diagnostics;

/// <summary>
/// Provides access to runtime diagnostics options for monitor previews.
/// </summary>
public static class PreviewDiagnostics
{
    private static PreviewDiagnosticsOptions _options = new();

    /// <summary>
    /// Gets the currently configured diagnostics options.
    /// </summary>
    public static PreviewDiagnosticsOptions Options => _options;

    /// <summary>
    /// Configures diagnostics options for the running session.
    /// </summary>
    /// <param name="options">The options to configure.</param>
    public static void Configure(PreviewDiagnosticsOptions? options)
    {
        _options = options?.Normalize() ?? new PreviewDiagnosticsOptions();
    }
}
