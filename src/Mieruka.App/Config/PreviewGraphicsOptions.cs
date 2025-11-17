using System;
using System.Text.Json.Serialization;
using Mieruka.Core.Diagnostics;

namespace Mieruka.App.Config;

/// <summary>
/// Represents persistent options that control monitor preview graphics behavior.
/// </summary>
public sealed record class PreviewGraphicsOptions
{
    /// <summary>
    /// Gets or sets the preferred graphics mode used when rendering monitor previews.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PreviewGraphicsMode Mode { get; init; } = PreviewGraphicsMode.Auto;

    /// <summary>
    /// Gets or sets diagnostic-specific options for monitor previews.
    /// </summary>
    public PreviewDiagnosticsOptions Diagnostics { get; init; } = new();

    /// <summary>
    /// Normalizes the current options ensuring all values are valid.
    /// </summary>
    /// <returns>A sanitized <see cref="PreviewGraphicsOptions"/> instance.</returns>
    public PreviewGraphicsOptions Normalize()
    {
        var normalizedMode = Enum.IsDefined(typeof(PreviewGraphicsMode), Mode)
            ? Mode
            : PreviewGraphicsMode.Auto;

        var normalizedDiagnostics = Diagnostics?.Normalize() ?? new PreviewDiagnosticsOptions();

        return this with
        {
            Mode = normalizedMode,
            Diagnostics = normalizedDiagnostics,
        };
    }
}

/// <summary>
/// Supported graphics capture modes for monitor previews.
/// </summary>
public enum PreviewGraphicsMode
{
    /// <summary>
    /// Uses automatic detection (GPU when available, with GDI fallback).
    /// </summary>
    Auto,

    /// <summary>
    /// Prefers GPU capture whenever possible.
    /// </summary>
    Gpu,

    /// <summary>
    /// Forces GDI capture, disabling GPU usage.
    /// </summary>
    Gdi,
}
