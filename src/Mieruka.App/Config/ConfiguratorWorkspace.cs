using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Mieruka.Core.Models;

namespace Mieruka.App.Config;

/// <summary>
/// Maintains an editable representation of the <see cref="GeneralConfig"/> file.
/// </summary>
internal sealed class ConfiguratorWorkspace
{
    private GeneralConfig _baseConfig;
    private readonly List<MonitorInfo> _monitors = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguratorWorkspace"/> class.
    /// </summary>
    /// <param name="config">Configuration that should be edited.</param>
    /// <param name="monitors">Collection of monitors available in the environment.</param>
    public ConfiguratorWorkspace(GeneralConfig config, IEnumerable<MonitorInfo>? monitors = null)
    {
        _baseConfig = config ?? new GeneralConfig();

        Applications = new BindingList<AppConfig>(new List<AppConfig>());
        Sites = new BindingList<SiteConfig>(new List<SiteConfig>());

        ApplyConfiguration(_baseConfig, monitors);
    }

    /// <summary>
    /// Gets the applications defined in the workspace.
    /// </summary>
    public BindingList<AppConfig> Applications { get; }

    /// <summary>
    /// Gets the sites defined in the workspace.
    /// </summary>
    public BindingList<SiteConfig> Sites { get; }

    /// <summary>
    /// Gets the monitors available in the workspace.
    /// </summary>
    public IReadOnlyList<MonitorInfo> Monitors => new ReadOnlyCollection<MonitorInfo>(_monitors);

    /// <summary>
    /// Attempts to update the monitor topology represented by the workspace.
    /// </summary>
    /// <param name="monitors">New monitor snapshot.</param>
    public void UpdateMonitors(IEnumerable<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        _monitors.Clear();
        _monitors.AddRange(monitors);
    }

    /// <summary>
    /// Replaces the configuration represented by the workspace.
    /// </summary>
    /// <param name="config">Configuration that should be loaded.</param>
    /// <param name="monitors">Optional monitor snapshot overriding the configuration monitors.</param>
    public void ApplyConfiguration(GeneralConfig config, IEnumerable<MonitorInfo>? monitors = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        _baseConfig = config;

        var monitorSnapshot = (monitors ?? config.Monitors).ToList();
        if (monitorSnapshot.Count == 0)
        {
            monitorSnapshot.AddRange(config.Monitors);
        }

        _monitors.Clear();
        _monitors.AddRange(monitorSnapshot);

        UpdateBindingList(Applications, config.Applications.Select(CloneApp));
        UpdateBindingList(Sites, config.Sites.Select(CloneSite));
    }

    /// <summary>
    /// Searches for a monitor based on its key.
    /// </summary>
    /// <param name="key">Monitor identifier.</param>
    /// <returns>Monitor information, when available.</returns>
    public MonitorInfo? FindMonitor(MonitorKey key)
    {
        return _monitors.FirstOrDefault(m => MonitorKeysEqual(m.Key, key));
    }

    /// <summary>
    /// Retrieves the window configuration for a given entry.
    /// </summary>
    /// <param name="entry">Entry that should be inspected.</param>
    /// <returns>The <see cref="WindowConfig"/> when present; otherwise, <c>null</c>.</returns>
    public WindowConfig? GetWindow(EntryReference entry)
    {
        return entry.Kind switch
        {
            EntryKind.Application => Applications.FirstOrDefault(a => string.Equals(a.Id, entry.Id, StringComparison.OrdinalIgnoreCase))?.Window,
            EntryKind.Site => Sites.FirstOrDefault(s => string.Equals(s.Id, entry.Id, StringComparison.OrdinalIgnoreCase))?.Window,
            _ => null,
        };
    }

    /// <summary>
    /// Applies a monitor assignment to the provided entry.
    /// </summary>
    /// <param name="entry">Entry that should be updated.</param>
    /// <param name="monitor">Target monitor.</param>
    /// <param name="bounds">Optional bounds relative to the monitor.</param>
    /// <returns><c>true</c> when the entry was updated; otherwise, <c>false</c>.</returns>
    public bool TryAssignEntryToMonitor(EntryReference entry, MonitorInfo monitor, Rectangle? bounds = null)
    {
        return entry.Kind switch
        {
            EntryKind.Application => UpdateApplication(entry.Id, app => ApplyWindow(app.Window, monitor, bounds)) is not null,
            EntryKind.Site => UpdateSite(entry.Id, site => ApplyWindow(site.Window, monitor, bounds)) is not null,
            _ => false,
        };
    }

    /// <summary>
    /// Applies a rectangular selection to an entry.
    /// </summary>
    /// <param name="entry">Entry that should be updated.</param>
    /// <param name="monitor">Monitor that received the selection.</param>
    /// <param name="selection">Bounds of the selected area.</param>
    /// <returns><c>true</c> when the entry was updated; otherwise, <c>false</c>.</returns>
    public bool TryApplySelection(EntryReference entry, MonitorInfo monitor, Rectangle selection)
    {
        var normalized = NormalizeSelection(selection, monitor);
        return TryAssignEntryToMonitor(entry, monitor, normalized);
    }

    /// <summary>
    /// Converts a window configuration into monitor-relative coordinates.
    /// </summary>
    /// <param name="window">Window configuration.</param>
    /// <param name="monitor">Monitor that owns the window.</param>
    /// <returns>Rectangle representing the configured area.</returns>
    public Rectangle GetSelectionRectangle(WindowConfig window, MonitorInfo monitor)
    {
        var monitorBounds = new Rectangle(0, 0, monitor.Width, monitor.Height);

        if (window.FullScreen)
        {
            return monitorBounds;
        }

        var x = window.X ?? 0;
        var y = window.Y ?? 0;
        var width = window.Width ?? monitor.Width;
        var height = window.Height ?? monitor.Height;
        var candidate = new Rectangle(x, y, width, height);
        var intersection = Rectangle.Intersect(monitorBounds, candidate);
        return intersection.Width > 0 && intersection.Height > 0 ? intersection : monitorBounds;
    }

    /// <summary>
    /// Materializes the configuration represented by the workspace.
    /// </summary>
    /// <returns>A new <see cref="GeneralConfig"/> instance containing the changes.</returns>
    public GeneralConfig BuildConfiguration()
    {
        return _baseConfig with
        {
            SchemaVersion = ConfigSchemaVersion.Latest,
            LegacyVersion = null,
            Monitors = _monitors.ToList(),
            Applications = Applications.ToList(),
            Sites = Sites.ToList(),
        };
    }

    private static AppConfig CloneApp(AppConfig app)
        => app with { Window = app.Window with { } };

    private static SiteConfig CloneSite(SiteConfig site)
        => site with { Window = site.Window with { } };

    private static void UpdateBindingList<T>(BindingList<T> target, IEnumerable<T> items)
    {
        target.RaiseListChangedEvents = false;

        try
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }
        finally
        {
            target.RaiseListChangedEvents = true;
            target.ResetBindings();
        }
    }

    private AppConfig? UpdateApplication(string id, Func<AppConfig, WindowConfig> updater)
    {
        for (var index = 0; index < Applications.Count; index++)
        {
            var app = Applications[index];
            if (!string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var window = updater(app);
            var updated = app with { Window = window };
            Applications[index] = updated;
            return updated;
        }

        return null;
    }

    private SiteConfig? UpdateSite(string id, Func<SiteConfig, WindowConfig> updater)
    {
        for (var index = 0; index < Sites.Count; index++)
        {
            var site = Sites[index];
            if (!string.Equals(site.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var window = updater(site);
            var updated = site with { Window = window };
            Sites[index] = updated;
            return updated;
        }

        return null;
    }

    private static WindowConfig ApplyWindow(WindowConfig source, MonitorInfo monitor, Rectangle? bounds)
    {
        var monitorBounds = new Rectangle(0, 0, monitor.Width, monitor.Height);

        if (bounds is null || bounds.Value == monitorBounds)
        {
            return source with
            {
                Monitor = monitor.Key,
                X = null,
                Y = null,
                Width = null,
                Height = null,
                FullScreen = true,
            };
        }

        var normalized = NormalizeSelection(bounds.Value, monitor);

        return source with
        {
            Monitor = monitor.Key,
            X = normalized.X,
            Y = normalized.Y,
            Width = normalized.Width,
            Height = normalized.Height,
            FullScreen = false,
        };
    }

    private static Rectangle NormalizeSelection(Rectangle selection, MonitorInfo monitor)
    {
        var monitorBounds = new Rectangle(0, 0, monitor.Width, monitor.Height);
        var intersection = Rectangle.Intersect(monitorBounds, selection);
        return intersection.Width <= 0 || intersection.Height <= 0 ? monitorBounds : intersection;
    }

    private static bool MonitorKeysEqual(MonitorKey left, MonitorKey right)
    {
        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }
}
