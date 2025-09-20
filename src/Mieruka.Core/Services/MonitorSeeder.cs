using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;

namespace Mieruka.Core.Services;

/// <summary>
/// Produces initial configuration artifacts for newly detected monitors.
/// </summary>
public sealed class MonitorSeeder
{
    /// <summary>
    /// Creates monitor definitions for the provided probes.
    /// </summary>
    public IReadOnlyList<MonitorInfo> CreateMonitors(IReadOnlyList<MonitorProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);

        if (probes.Count == 0)
        {
            return Array.Empty<MonitorInfo>();
        }

        var monitors = new List<MonitorInfo>(probes.Count);
        for (var index = 0; index < probes.Count; index++)
        {
            var probe = probes[index] ?? throw new ArgumentException("Probe collection cannot contain null entries.", nameof(probes));
            var bounds = SanitizeBounds(probe.Bounds);

            monitors.Add(new MonitorInfo
            {
                Key = new MonitorKey
                {
                    DeviceId = probe.DeviceName ?? string.Empty,
                    DisplayIndex = index,
                },
                Name = ResolveMonitorName(probe, index),
                DeviceName = probe.DeviceName ?? string.Empty,
                Width = bounds.Width,
                Height = bounds.Height,
                Scale = probe.Scale <= 0 ? 1.0 : probe.Scale,
                IsPrimary = probe.IsPrimary,
            });
        }

        return new ReadOnlyCollection<MonitorInfo>(monitors);
    }

    /// <summary>
    /// Returns the default set of layout presets.
    /// </summary>
    public IList<ZonePreset> CreateDefaultZonePresets()
        => new List<ZonePreset>(ZonePreset.Defaults);

    /// <summary>
    /// Updates the configuration with monitor information and optional preset restoration.
    /// </summary>
    /// <param name="config">Configuration that should be updated.</param>
    /// <param name="probes">Detected monitor probes.</param>
    /// <param name="resetPresets">When <c>true</c>, replaces the preset collection with defaults.</param>
    public GeneralConfig ApplySeeds(GeneralConfig config, IReadOnlyList<MonitorProbe> probes, bool resetPresets)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(probes);

        var monitors = CreateMonitors(probes);
        var presets = resetPresets || config.ZonePresets.Count == 0
            ? CreateDefaultZonePresets()
            : new List<ZonePreset>(config.ZonePresets);

        return config with
        {
            Monitors = monitors.ToList(),
            ZonePresets = presets,
        };
    }

    private static Rectangle SanitizeBounds(Rectangle bounds)
    {
        var width = Math.Max(1, bounds.Width);
        var height = Math.Max(1, bounds.Height);
        return new Rectangle(0, 0, width, height);
    }

    private static string ResolveMonitorName(MonitorProbe probe, int index)
    {
        if (!string.IsNullOrWhiteSpace(probe.FriendlyName))
        {
            return probe.FriendlyName;
        }

        return $"Monitor {index + 1}";
    }
}
