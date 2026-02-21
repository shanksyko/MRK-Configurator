#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Interop;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
  private void RebuildSimulationOverlays()
  {
    using var logicalDepth = new MonitorPreviewDisplay.PreviewLogicalScope(nameof(RebuildSimulationOverlays), _logger);
    if (!logicalDepth.Entered)
    {
      _logger.Error("RebuildSimulationOverlays: preview logical depth limit reached; aborting rebuild");
      return;
    }

    using var depth = new MonitorPreviewDisplay.PreviewCallScope(nameof(RebuildSimulationOverlays), _logger);
    if (!depth.Entered)
    {
      _logger.Error("RebuildSimulationOverlays: depth limit reached; aborting rebuild");
      return;
    }

    var currentDepth = Interlocked.Increment(ref _simOverlaysDepth);
    if (currentDepth > 8)
    {
      _logger.Error("sim_overlays_depth_limit_reached depth={Depth} limit={Limit}", currentDepth, 8);
      Interlocked.Decrement(ref _simOverlaysDepth);
      return;
    }

    if (_inRebuildSimulationOverlays)
    {
      _logger.Debug("RebuildSimulationOverlays: recursion blocked");
      Interlocked.Decrement(ref _simOverlaysDepth);
      return;
    }

    _inRebuildSimulationOverlays = true;
    try
    {
      if (monitorPreviewDisplay is null)
      {
        _logger.Debug("RebuildSimulationOverlays: skip missing preview display");
        return;
      }

      var monitor = GetSelectedMonitor();
      if (monitor is null)
      {
        monitorPreviewDisplay.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
        _logger.Debug("RebuildSimulationOverlays: exit no monitor selected");
        return;
      }

      var monitorId = MonitorIdentifier.Create(monitor);
      if (string.IsNullOrWhiteSpace(monitorId))
      {
        monitorPreviewDisplay.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
        _logger.Debug(
            "RebuildSimulationOverlays: exit invalid monitor identifier monitor={MonitorName}",
            monitor.Name ?? string.Empty);
        return;
      }

      var current = ConstruirPrograma();
      var overlayApps = new List<ProgramaConfig>();
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      foreach (var app in CurrentProfileItems())
      {
        if (app is null)
        {
          continue;
        }

        var candidate = app;
        if (!string.IsNullOrWhiteSpace(current.Id) &&
            string.Equals(app.Id, current.Id, StringComparison.OrdinalIgnoreCase))
        {
          candidate = current;
        }

        if (string.IsNullOrWhiteSpace(candidate.Id) || !seen.Add(candidate.Id))
        {
          continue;
        }

        overlayApps.Add(candidate);
      }

      if (!string.IsNullOrWhiteSpace(current.Id))
      {
        if (seen.Add(current.Id))
        {
          overlayApps.Add(current);
        }
      }
      else
      {
        overlayApps.Add(current);
      }

      var overlays = new List<MonitorPreviewDisplay.SimRect>();
      var order = 1;

      foreach (var app in overlayApps)
      {
        var isCurrent = !string.IsNullOrWhiteSpace(current.Id) &&
            string.Equals(app.Id, current.Id, StringComparison.OrdinalIgnoreCase);

        if (!app.AutoStart && !isCurrent)
        {
          continue;
        }

        var resolvedMonitor = ResolveMonitorForApp(app);
        if (resolvedMonitor is null && isCurrent)
        {
          resolvedMonitor = monitor;
        }

        if (resolvedMonitor is null)
        {
          continue;
        }

        var resolvedId = MonitorIdentifier.Create(resolvedMonitor);
        if (string.IsNullOrWhiteSpace(resolvedId) ||
            !string.Equals(resolvedId, monitorId, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        var relativeBounds = CalculateMonitorRelativeBounds(app.Window, resolvedMonitor);
        if (relativeBounds.Width < MinimumOverlayDimension || relativeBounds.Height < MinimumOverlayDimension)
        {
          _logger.Debug(
              "RebuildSimulationOverlays: ignored_zero_bound_overlay bounds={Bounds} appId={AppId} monitorId={MonitorId}",
              relativeBounds,
              app.Id ?? string.Empty,
              monitorId);
          continue;
        }

        var baseColor = ResolveSimulationColor(app.Id);
        var label = string.IsNullOrWhiteSpace(app.Window.Title) ? app.Id : app.Window.Title;

        overlays.Add(new MonitorPreviewDisplay.SimRect
        {
          MonRel = relativeBounds,
          Color = baseColor,
          Order = order,
          Title = label ?? string.Empty,
          RequiresNetwork = app.RequiresNetwork,
          AskBefore = app.AskBeforeLaunch,
        });

        order++;
      }

      monitorPreviewDisplay.SetSimulationRects(overlays);
      _logger.Debug(
          "RebuildSimulationOverlays: exit monitorId={MonitorId} overlayCandidates={OverlayCandidates} overlaysRendered={OverlaysRendered} currentAppId={CurrentAppId}",
          monitorId,
          overlayApps.Count,
          overlays.Count,
          current.Id ?? string.Empty);
    }
    finally
    {
      _inRebuildSimulationOverlays = false;
      var depthAfter = Interlocked.Decrement(ref _simOverlaysDepth);
      if (depthAfter < 0)
      {
        Interlocked.Exchange(ref _simOverlaysDepth, 0);
      }
    }
  }

  private void CacheOverlaySnapshot(WindowPreviewSnapshot snapshot)
  {
    _lastOverlayBounds = snapshot.Bounds;
    _lastOverlayMonitorId = snapshot.MonitorId;
    _lastOverlayFullScreen = snapshot.IsFullScreen;
    _hasCachedOverlayBounds = true;
  }

  private bool ShouldApplyOverlay(WindowPreviewSnapshot snapshot)
  {
    if (!_hasCachedOverlayBounds)
    {
      return true;
    }

    if (!string.Equals(snapshot.MonitorId, _lastOverlayMonitorId, StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (snapshot.IsFullScreen != _lastOverlayFullScreen)
    {
      return true;
    }

    return !AreBoundsClose(snapshot.Bounds, _lastOverlayBounds, 1);
  }

  private static bool AreBoundsClose(Drawing.Rectangle current, Drawing.Rectangle previous, int tolerance)
  {
    return Math.Abs(current.X - previous.X) <= tolerance &&
           Math.Abs(current.Y - previous.Y) <= tolerance &&
           Math.Abs(current.Width - previous.Width) <= tolerance &&
           Math.Abs(current.Height - previous.Height) <= tolerance;
  }

  private MonitorInfo? ResolveMonitorForApp(ProgramaConfig app)
  {
    if (!string.IsNullOrWhiteSpace(app.TargetMonitorStableId))
    {
      var byStableId = WindowPlacementHelper.GetMonitorByStableId(_monitors, app.TargetMonitorStableId);
      if (byStableId is not null)
      {
        return byStableId;
      }
    }

    var monitorKeyId = MonitorIdentifier.Create(app.Window.Monitor);
    if (!string.IsNullOrWhiteSpace(monitorKeyId))
    {
      return WindowPlacementHelper.ResolveMonitor(null, _monitors, app.Window);
    }

    return null;
  }

  private static Drawing.Rectangle CalculateMonitorRelativeBounds(WindowConfig window, MonitorInfo monitor)
  {
    var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
    var monitorWidth = monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width;
    var monitorHeight = monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height;

    monitorWidth = Math.Max(1, monitorWidth);
    monitorHeight = Math.Max(1, monitorHeight);

    if (window.FullScreen)
    {
      return new Drawing.Rectangle(0, 0, monitorWidth, monitorHeight);
    }

    var width = window.Width ?? monitorWidth;
    var height = window.Height ?? monitorHeight;
    width = Math.Clamp(width, 1, monitorWidth);
    height = Math.Clamp(height, 1, monitorHeight);

    var x = window.X ?? 0;
    var y = window.Y ?? 0;
    x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
    y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

    var relative = new Drawing.Rectangle(x, y, width, height);

    var bounds = monitor.Bounds;
    var workArea = monitor.WorkArea;
    if (bounds.Width > 0 && bounds.Height > 0 && workArea.Width > 0 && workArea.Height > 0)
    {
      var absolute = new Drawing.Rectangle(bounds.Left + relative.X, bounds.Top + relative.Y, relative.Width, relative.Height);
      var clamped = DisplayUtils.ClampToWorkArea(absolute, workArea);
      relative = new Drawing.Rectangle(
          clamped.Left - bounds.Left,
          clamped.Top - bounds.Top,
          clamped.Width,
          clamped.Height);
    }

    return relative;
  }

  private static Drawing.Color ResolveSimulationColor(string? id)
  {
    if (SimulationPalette.Length == 0)
    {
      return Drawing.Color.DodgerBlue;
    }

    if (string.IsNullOrWhiteSpace(id))
    {
      return SimulationPalette[0];
    }

    const uint basis = 2166136261u;
    const uint prime = 16777619u;
    uint hash = basis;

    foreach (var ch in id)
    {
      hash ^= char.ToUpperInvariant(ch);
      hash *= prime;
    }

    var index = (int)(hash % (uint)SimulationPalette.Length);
    return SimulationPalette[index];
  }

  private void AdjustMonitorPreviewWidth()
  {
    if (tlpMonitorPreview is null || tpJanela is null)
    {
      return;
    }

    var availableWidth = tpJanela.ClientSize.Width;
    if (availableWidth <= 0)
    {
      return;
    }

    var minimumWidth = tlpMonitorPreview.MinimumSize.Width > 0 ? tlpMonitorPreview.MinimumSize.Width : 420;
    if (availableWidth <= minimumWidth)
    {
      tlpMonitorPreview.Width = availableWidth;
      return;
    }

    var desired = Math.Max(minimumWidth, availableWidth / 2);
    var maxAllowed = Math.Max(minimumWidth, availableWidth - 320);
    var width = Math.Min(desired, maxAllowed);
    width = Math.Max(minimumWidth, Math.Min(width, availableWidth));
    tlpMonitorPreview.Width = width;
  }

  private MonitorInfo? GetSelectedMonitor()
  {
    if (_selectedMonitorInfo is not null)
    {
      return _selectedMonitorInfo;
    }

    return cboMonitores?.SelectedItem is MonitorOption option ? option.Monitor : null;
  }

  private void UpdatePreviewVisibility()
  {
    var preview = monitorPreviewDisplay;
    if (preview is null || preview.IsDisposed)
    {
      return;
    }

    var previewTabSelected = tabEditor is { SelectedTab: { } selectedTab }
        && tpJanela is not null
        && ReferenceEquals(selectedTab, tpJanela)
        && tabEditor.TabPages.Contains(tpJanela);

    preview.SetPreviewVisibility(previewTabSelected);
  }

  private static WindowConfig ClampWindowBounds(WindowConfig window, MonitorInfo monitor)
  {
    var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
    var monitorWidth = monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width;
    var monitorHeight = monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height;

    monitorWidth = Math.Max(1, monitorWidth);
    monitorHeight = Math.Max(1, monitorHeight);

    var width = window.Width;
    var height = window.Height;
    var x = window.X;
    var y = window.Y;

    if (width is int w)
    {
      width = Math.Clamp(w, 1, monitorWidth);
    }

    if (height is int h)
    {
      height = Math.Clamp(h, 1, monitorHeight);
    }

    if (x is int posX && width is int wValue)
    {
      var maxX = Math.Max(0, monitorWidth - wValue);
      x = Math.Clamp(posX, 0, maxX);
    }

    if (y is int posY && height is int hValue)
    {
      var maxY = Math.Max(0, monitorHeight - hValue);
      y = Math.Clamp(posY, 0, maxY);
    }

    return window with
    {
      X = x,
      Y = y,
      Width = width,
      Height = height,
    };
  }

  private void ApplyRelativeWindowToNewMonitor(WindowConfig previousWindow, MonitorInfo previousMonitor, MonitorInfo newMonitor)
  {
    if (previousWindow.FullScreen)
    {
      return;
    }

    var zone = WindowPlacementHelper.CreateZoneFromWindow(previousWindow, previousMonitor);
    var relative = CalculateRelativeRectangle(zone, newMonitor);
    ApplyBoundsToInputs(relative);
  }

  private static Drawing.Rectangle CalculateRelativeRectangle(WindowPlacementHelper.ZoneRect zone, MonitorInfo monitor)
  {
    var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
    var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
    var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

    var width = Math.Max(1, (int)Math.Round(monitorWidth * (zone.WidthPercentage / 100d), MidpointRounding.AwayFromZero));
    var height = Math.Max(1, (int)Math.Round(monitorHeight * (zone.HeightPercentage / 100d), MidpointRounding.AwayFromZero));
    var x = (int)Math.Round(monitorWidth * (zone.LeftPercentage / 100d), MidpointRounding.AwayFromZero);
    var y = (int)Math.Round(monitorHeight * (zone.TopPercentage / 100d), MidpointRounding.AwayFromZero);

    x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
    y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

    return new Drawing.Rectangle(x, y, width, height);
  }

  private void ApplyBoundsToInputs(Drawing.Rectangle bounds)
  {
    if (chkJanelaTelaCheia.Checked)
    {
      return;
    }

    if (nudJanelaX is not null)
    {
      nudJanelaX.Value = AjustarRange(nudJanelaX, bounds.X);
    }

    if (nudJanelaY is not null)
    {
      nudJanelaY.Value = AjustarRange(nudJanelaY, bounds.Y);
    }

    if (nudJanelaLargura is not null)
    {
      nudJanelaLargura.Value = AjustarRange(nudJanelaLargura, bounds.Width);
    }

    if (nudJanelaAltura is not null)
    {
      nudJanelaAltura.Value = AjustarRange(nudJanelaAltura, bounds.Height);
    }
  }

  private static int TryGetRefreshRate(string? deviceName)
  {
    if (!OperatingSystem.IsWindows())
    {
      return 0;
    }

    if (string.IsNullOrWhiteSpace(deviceName))
    {
      return 0;
    }

    try
    {
      var mode = new DEVMODE
      {
        dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>(),
      };

      return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)
          ? mode.dmDisplayFrequency
          : 0;
    }
    catch (DllNotFoundException)
    {
      return 0;
    }
    catch (EntryPointNotFoundException)
    {
      return 0;
    }
  }
}
