using System;
using System.Globalization;
using System.Text;
using Mieruka.Core.Models;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Provides utilities to generate stable monitor identifiers based on adapter and target metadata.
/// </summary>
public static class MonitorIdentifier
{
    /// <summary>
    /// Creates an identifier using the information stored in <see cref="MonitorInfo"/>.
    /// </summary>
    public static string Create(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return Create(monitor.Key);
    }

    /// <summary>
    /// Creates an identifier using the information stored in <see cref="MonitorKey"/>.
    /// </summary>
    public static string Create(MonitorKey? key)
    {
        if (key is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append(key.AdapterLuidHigh.ToString("X8", CultureInfo.InvariantCulture));
        builder.Append('-');
        builder.Append(key.AdapterLuidLow.ToString("X8", CultureInfo.InvariantCulture));
        builder.Append('-');
        builder.Append(key.TargetId.ToString("X8", CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(key.DeviceId))
        {
            builder.Append('-');
            builder.Append(key.DeviceId);
        }

        return builder.ToString();
    }
}
