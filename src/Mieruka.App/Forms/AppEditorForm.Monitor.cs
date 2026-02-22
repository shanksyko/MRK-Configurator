#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
    private void rbExe_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyAppTypeUI();
    }

    private void rbBrowser_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyAppTypeUI();
    }

    private void chkCycleRedeDisponivel_CheckedChanged(object? sender, EventArgs e)
    {
        RefreshSimRectTooltips();
    }

    private void RefreshMonitorSnapshot()
    {
        List<MonitorInfo> merged;

        try
        {
            var installed = DisplayService.GetMonitors();
            merged = installed.Count > 0
                ? MergeMonitors(installed, _providedMonitors, _monitors)
                : MergeMonitors(_providedMonitors, _monitors);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Falha ao detectar monitores instalados; usando fallback.");
            merged = MergeMonitors(_providedMonitors, _monitors);
        }

        _monitors.Clear();
        _monitors.AddRange(merged);
    }

    private static List<MonitorInfo> MergeMonitors(params IEnumerable<MonitorInfo>?[] sources)
    {
        var result = new List<MonitorInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var monitor in source)
            {
                if (monitor is null)
                {
                    continue;
                }

                var key = GetMonitorKey(monitor);
                if (seen.Add(key))
                {
                    result.Add(monitor);
                }
            }
        }

        return result;
    }

    private static string GetMonitorKey(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.StableId))
        {
            return monitor.StableId;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            return monitor.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId))
        {
            return monitor.Key.DeviceId;
        }

        var bounds = monitor.Bounds;
        return string.Join(
            '|',
            monitor.Key.AdapterLuidHigh.ToString("X8", CultureInfo.InvariantCulture),
            monitor.Key.AdapterLuidLow.ToString("X8", CultureInfo.InvariantCulture),
            monitor.Key.TargetId.ToString(CultureInfo.InvariantCulture),
            bounds.X.ToString(CultureInfo.InvariantCulture),
            bounds.Y.ToString(CultureInfo.InvariantCulture),
            bounds.Width.ToString(CultureInfo.InvariantCulture),
            bounds.Height.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatMonitorDisplayName(MonitorInfo monitor)
    {
        var deviceName = !string.IsNullOrWhiteSpace(monitor.DeviceName)
            ? monitor.DeviceName
            : (!string.IsNullOrWhiteSpace(monitor.Name) ? monitor.Name : "Monitor");

        var width = monitor.Width > 0 ? monitor.Width : monitor.Bounds.Width;
        var height = monitor.Height > 0 ? monitor.Height : monitor.Bounds.Height;
        var resolution = width > 0 && height > 0 ? $"{width}x{height}" : "?x?";

        var refresh = TryGetRefreshRate(monitor.DeviceName);
        var refreshText = refresh > 0 ? $"{refresh}Hz" : "?Hz";

        return $"{deviceName} {resolution} @ {refreshText}";
    }

    private void PopulateMonitorCombo(ProgramaConfig? programa)
    {
        if (cboMonitores is null)
        {
            return;
        }

        RefreshMonitorSnapshot();
        _suppressMonitorComboEvents = true;

        try
        {
            cboMonitores.Items.Clear();

            if (_monitors.Count == 0)
            {
                cboMonitores.Items.Add(MonitorOption.Empty());
                cboMonitores.SelectedIndex = 0;
                _selectedMonitorInfo = null;
                _selectedMonitorId = null;
            }
            else
            {
                foreach (var monitor in _monitors)
                {
                    var monitorId = MonitorIdentifier.Create(monitor);
                    var displayName = FormatMonitorDisplayName(monitor);
                    cboMonitores.Items.Add(new MonitorOption(monitorId, monitor, displayName));
                }

                var candidates = new[]
                {
                    _preferredMonitorId,
                    programa?.TargetMonitorStableId,
                    MonitorIdentifier.Create(programa?.Window?.Monitor),
                };

                var selectionApplied = false;
                foreach (var candidate in candidates)
                {
                    if (SelectMonitorById(candidate))
                    {
                        selectionApplied = true;
                        break;
                    }
                }

                if (!selectionApplied && cboMonitores.Items.Count > 0)
                {
                    cboMonitores.SelectedIndex = 0;
                }
            }
        }
        finally
        {
            _suppressMonitorComboEvents = false;
        }

        _ = UpdateMonitorPreviewSafelyAsync();
    }

    private bool SelectMonitorById(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || cboMonitores is null)
        {
            return false;
        }

        for (var index = 0; index < cboMonitores.Items.Count; index++)
        {
            if (cboMonitores.Items[index] is not MonitorOption option || option.MonitorId is null)
            {
                continue;
            }

            if (string.Equals(option.MonitorId, identifier, StringComparison.OrdinalIgnoreCase))
            {
                cboMonitores.SelectedIndex = index;
                return true;
            }
        }

        return false;
    }
}
