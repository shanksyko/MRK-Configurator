using System;

namespace Mieruka.Core.Services;

/// <summary>
/// Telemetry sink that discards all events.
/// </summary>
public sealed class NullTelemetry : ITelemetry
{
    public static readonly NullTelemetry Instance = new();

    private NullTelemetry()
    {
    }

    public void Info(string message, Exception? exception = null)
    {
        // Intentionally left blank.
    }

    public void Warn(string message, Exception? exception = null)
    {
        // Intentionally left blank.
    }

    public void Error(string message, Exception? exception = null)
    {
        // Intentionally left blank.
    }
}
