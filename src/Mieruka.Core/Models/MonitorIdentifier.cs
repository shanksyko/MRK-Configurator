using System;
using System.Globalization;
using System.Linq;

namespace Mieruka.Core.Models;

/// <summary>
/// Provides helpers to generate and parse stable monitor identifiers.
/// </summary>
public static class MonitorIdentifier
{
    private const char AdapterSeparator = ':';
    private const char SegmentSeparator = '/';
    private const char DeviceSeparator = '|';

    /// <summary>
    /// Creates a monitor identifier using adapter and target information when available.
    /// </summary>
    public static string Create(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        return Create(monitor.Key, monitor.DeviceName);
    }

    /// <summary>
    /// Creates a monitor identifier using the provided key and optional device name.
    /// </summary>
    public static string Create(MonitorKey? key, string? deviceName = null)
    {
        if (key is not { } k)
        {
            return string.IsNullOrWhiteSpace(deviceName) ? string.Empty : deviceName;
        }

        var hasAdapterInfo = k.AdapterLuidHigh != 0 || k.AdapterLuidLow != 0 || k.TargetId != 0;
        if (!hasAdapterInfo)
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                return deviceName;
            }

            if (!string.IsNullOrWhiteSpace(k.DeviceId))
            {
                return k.DeviceId;
            }

            return string.Empty;
        }

        var adapter = string.Join(
            AdapterSeparator,
            k.AdapterLuidHigh.ToString("X8", CultureInfo.InvariantCulture),
            k.AdapterLuidLow.ToString("X8", CultureInfo.InvariantCulture));

        var identifier = string.Join(
            SegmentSeparator,
            adapter,
            k.TargetId.ToString("X8", CultureInfo.InvariantCulture));

        var device = !string.IsNullOrWhiteSpace(deviceName)
            ? deviceName
            : k.DeviceId;

        return string.IsNullOrWhiteSpace(device)
            ? identifier
            : string.Concat(identifier, DeviceSeparator, device);
    }

    /// <summary>
    /// Parses the provided identifier into a <see cref="MonitorKey"/>.
    /// </summary>
    public static bool TryParse(string? identifier, out MonitorKey key, out string? deviceName)
    {
        key = new MonitorKey();
        deviceName = null;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var trimmed = identifier.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        // Accept raw device identifiers (for example "\\\\.\\DISPLAY1").
        if (!trimmed.Contains(SegmentSeparator) && !trimmed.Contains(AdapterSeparator))
        {
            deviceName = trimmed;
            key = key with { DeviceId = trimmed };
            return true;
        }

        var parts = trimmed.Split(DeviceSeparator, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            deviceName = parts[1];
        }

        var core = parts[0];
        if (TryParseModernFormat(core, ref key))
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                key = key with { DeviceId = deviceName };
            }

            return true;
        }

        if (TryParseLegacyFormat(core, ref key, deviceName))
        {
            if (!string.IsNullOrWhiteSpace(deviceName) && string.IsNullOrWhiteSpace(key.DeviceId))
            {
                key = key with { DeviceId = deviceName };
            }

            return true;
        }

        // As a last resort fallback to interpreting the identifier as a device name.
        deviceName = trimmed;
        key = key with { DeviceId = trimmed };
        return true;
    }

    /// <summary>
    /// Normalizes the provided identifier into its canonical representation.
    /// </summary>
    /// <remarks>
    /// When the identifier cannot be parsed it is returned trimmed, ensuring a stable
    /// representation suitable for lookups such as GPU backoff keys.
    /// </remarks>
    public static string Normalize(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return string.Empty;
        }

        if (TryParse(identifier, out var key, out var deviceName))
        {
            return Create(key, deviceName);
        }

        return identifier.Trim();
    }

    private static bool TryParseModernFormat(string candidate, ref MonitorKey key)
    {
        var segments = candidate.Split(SegmentSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        var adapterSegments = segments[0].Split(AdapterSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (adapterSegments.Length != 2)
        {
            return false;
        }

        if (!TryParseHex(adapterSegments[0], out var luidHigh) ||
            !TryParseHex(adapterSegments[1], out var luidLow) ||
            !TryParseHex(segments[1], out var targetId))
        {
            return false;
        }

        key = key with
        {
            AdapterLuidHigh = luidHigh,
            AdapterLuidLow = luidLow,
            TargetId = targetId,
        };

        return true;
    }

    private static bool TryParseLegacyFormat(string candidate, ref MonitorKey key, string? deviceName)
    {
        var segments = candidate.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            return false;
        }

        if (!TryParseHex(segments[0], out var luidHigh) ||
            !TryParseHex(segments[1], out var luidLow) ||
            !TryParseHex(segments[2], out var targetId))
        {
            return false;
        }

        key = key with
        {
            AdapterLuidHigh = luidHigh,
            AdapterLuidLow = luidLow,
            TargetId = targetId,
        };

        if (segments.Length > 3)
        {
            var remaining = string.Join('-', segments.Skip(3));
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                key = key with { DeviceId = remaining };
            }
        }
        else if (!string.IsNullOrWhiteSpace(deviceName))
        {
            key = key with { DeviceId = deviceName };
        }

        return true;
    }

    private static bool TryParseHex(string value, out int result)
    {
        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }
}
