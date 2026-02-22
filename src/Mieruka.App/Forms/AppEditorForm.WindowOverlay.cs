#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using System.Threading.Tasks;
using Mieruka.App.Interop;
using Mieruka.App.Services;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
  private bool ClampWindowInputsToMonitor(
      Drawing.Point? pointer,
      bool allowFullScreen = false,
      (WinForms.NumericUpDown X, WinForms.NumericUpDown Y, WinForms.NumericUpDown Width, WinForms.NumericUpDown Height)? windowInputs = null)
  {
    var fullScreenToggle = chkJanelaTelaCheia;
    if (!allowFullScreen && fullScreenToggle is not null && !fullScreenToggle.IsDisposed && fullScreenToggle.Checked)
    {
      return false;
    }

    (WinForms.NumericUpDown X, WinForms.NumericUpDown Y, WinForms.NumericUpDown Width, WinForms.NumericUpDown Height) inputs;
    if (windowInputs is { } provided)
    {
      inputs = provided;
    }
    else if (!TryGetWindowInputs(out var xInput, out var yInput, out var widthInput, out var heightInput))
    {
      return false;
    }
    else
    {
      inputs = (xInput, yInput, widthInput, heightInput);
    }

    var (xControl, yControl, widthControl, heightControl) = inputs;

    var monitor = GetSelectedMonitor();
    if (monitor is null)
    {
      return false;
    }

    var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
    var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
    var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

    var changed = false;

    _suppressWindowInputHandlers = true;
    using var redrawScope = RedrawScope.Begin(xControl, yControl, widthControl, heightControl);
    try
    {
      var width = (int)widthControl.Value;
      if (monitorWidth > 0)
      {
        var clampedWidth = Math.Clamp(width, 1, monitorWidth);
        changed |= UpdateNumericControl(widthControl, clampedWidth);
        width = clampedWidth;
      }

      var height = (int)heightControl.Value;
      if (monitorHeight > 0)
      {
        var clampedHeight = Math.Clamp(height, 1, monitorHeight);
        changed |= UpdateNumericControl(heightControl, clampedHeight);
        height = clampedHeight;
      }

      if (pointer is Drawing.Point target)
      {
        var targetX = target.X;
        if (monitorWidth > 0)
        {
          var maxX = Math.Max(0, monitorWidth - width);
          targetX = Math.Clamp(targetX, 0, maxX);
        }

        var targetY = target.Y;
        if (monitorHeight > 0)
        {
          var maxY = Math.Max(0, monitorHeight - height);
          targetY = Math.Clamp(targetY, 0, maxY);
        }

        changed |= UpdateNumericControl(xControl, targetX);
        changed |= UpdateNumericControl(yControl, targetY);
      }
      else
      {
        if (monitorWidth > 0)
        {
          var currentX = (int)xControl.Value;
          var maxX = Math.Max(0, monitorWidth - width);
          var clampedX = Math.Clamp(currentX, 0, maxX);
          changed |= UpdateNumericControl(xControl, clampedX);
        }

        if (monitorHeight > 0)
        {
          var currentY = (int)yControl.Value;
          var maxY = Math.Max(0, monitorHeight - height);
          var clampedY = Math.Clamp(currentY, 0, maxY);
          changed |= UpdateNumericControl(yControl, clampedY);
        }
      }
    }
    finally
    {
      _suppressWindowInputHandlers = false;
    }

    if (changed)
    {
      InvalidateWindowPreviewOverlay();
    }

    return changed;
  }

  private bool TryGetWindowInputs(
      out WinForms.NumericUpDown xInput,
      out WinForms.NumericUpDown yInput,
      out WinForms.NumericUpDown widthInput,
      out WinForms.NumericUpDown heightInput)
  {
    if (nudJanelaX is WinForms.NumericUpDown x && !x.IsDisposed &&
        nudJanelaY is WinForms.NumericUpDown y && !y.IsDisposed &&
        nudJanelaLargura is WinForms.NumericUpDown width && !width.IsDisposed &&
        nudJanelaAltura is WinForms.NumericUpDown height && !height.IsDisposed)
    {
      xInput = x;
      yInput = y;
      widthInput = width;
      heightInput = height;
      return true;
    }

    xInput = null!;
    yInput = null!;
    widthInput = null!;
    heightInput = null!;
    return false;
  }

  private static bool UpdateNumericControl(WinForms.NumericUpDown control, int value)
  {
    var adjusted = AjustarRange(control, value);
    if (control.Value != adjusted)
    {
      control.Value = adjusted;
      return true;
    }

    return false;
  }

  private sealed class RedrawScope : IDisposable
  {
    private const int WmSetRedraw = 0x000B;
    private readonly WinForms.Control[] _controls;

    private RedrawScope(WinForms.Control[] controls)
    {
      _controls = controls;

      foreach (var control in controls)
      {
        if (control.IsHandleCreated)
        {
          SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }
      }
    }

    public static RedrawScope Begin(params WinForms.Control[] controls)
    {
      return new RedrawScope(controls);
    }

    public void Dispose()
    {
      foreach (var control in _controls)
      {
        try
        {
          if (!control.IsHandleCreated)
          {
            continue;
          }

          SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
          control.Invalidate();
        }
        catch (ObjectDisposedException)
        {
          // Control was disposed between handle check and invalidation.
        }
      }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
  }

  private void InvalidateWindowPreviewOverlay()
  {
    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; skipping window/overlay update.");
      return;
    }

    if (preview.IsDisposed)
    {
      _windowBoundsDebounce.Stop();
      _windowBoundsDebouncePending = false;
      _windowPreviewRebuildScheduled = false;
      return;
    }

    _windowBoundsDebounce.Stop();
    _windowBoundsDebouncePending = false;

    if (_invalidSnapshotAttempts >= 5)
    {
      _logger.Debug("CaptureWindowPreviewSnapshot skipped_due_to_invalid_bounds_limit");
      return;
    }

    if (preview.IsPreviewRunning)
    {
      _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

      _windowBoundsDebounce?.Stop();
      _windowBoundsDebouncePending = false;
      _windowPreviewRebuildScheduled = false;

      return;
    }

    var snapshot = CaptureWindowPreviewSnapshot();
    var now = _windowPreviewStopwatch.Elapsed;

    if (!_hasWindowPreviewSnapshot)
    {
      ApplyWindowPreviewSnapshot(snapshot, now);
      return;
    }

    if (snapshot.Equals(_windowPreviewSnapshot))
    {
      if (_windowPreviewRebuildScheduled)
      {
        preview.Invalidate();
      }

      return;
    }

    var elapsed = now - _lastWindowPreviewRebuild;
    if (elapsed < WindowPreviewRebuildInterval)
    {
      if (!_windowPreviewRebuildScheduled)
      {
        _windowPreviewRebuildScheduled = true;
        var delay = WindowPreviewRebuildInterval - elapsed;
        ScheduleWindowPreviewRebuild(delay);
      }

      preview.Invalidate();
      return;
    }

    ApplyWindowPreviewSnapshot(snapshot, now);
  }

  private void QueueWindowOverlayUpdate()
  {
    if (_suppressWindowInputHandlers)
    {
      return;
    }

    _ = ClampWindowInputsToMonitor(null, allowFullScreen: true);

    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; skipping window/overlay update.");
      return;
    }

    if (preview.IsPreviewRunning)
    {
      _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

      _windowBoundsDebounce?.Stop();
      _windowBoundsDebouncePending = false;
      _windowPreviewRebuildScheduled = false;
      return;
    }

    var snapshot = CaptureWindowPreviewSnapshot();
    var bounds = snapshot.Bounds;
    var nowUtc = DateTime.UtcNow;
    if (!bounds.Equals(_lastWindowRectLog) || (nowUtc - _lastWindowRectLogUtc).TotalMilliseconds >= 500)
    {
      _logger.Debug(
          "RectValueChanged: x={X}, y={Y}, w={W}, h={H}",
          bounds.X,
          bounds.Y,
          bounds.Width,
          bounds.Height);
      _lastWindowRectLog = bounds;
      _lastWindowRectLogUtc = nowUtc;
    }

    _windowBoundsDebouncePending = true;
    _windowBoundsDebounce.Stop();
    _windowBoundsDebounce.Start();
  }

  private void MonitorPreviewDisplayOnPreviewStarted(object? sender, EventArgs e)
  {
    _logger.Debug("monitor_preview_started: disabling snapshot pipeline");
    ResetSnapshotPipelineState();
  }

  private void MonitorPreviewDisplayOnPreviewStopped(object? sender, EventArgs e)
  {
    _logger.Debug("monitor_preview_stopped: re-enabling snapshot pipeline");
    ResetSnapshotPipelineState();
    InvalidateWindowPreviewOverlay();
  }

  private void WindowBoundsDebounceOnTick(object? sender, EventArgs e)
  {
    _windowBoundsDebounce.Stop();

    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; skipping window/overlay update.");
      return;
    }

    if (preview.IsPreviewRunning)
    {
      _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

      _windowBoundsDebounce?.Stop();
      _windowBoundsDebouncePending = false;
      _windowPreviewRebuildScheduled = false;
      return;
    }

    if (!_windowBoundsDebouncePending)
    {
      return;
    }

    _windowBoundsDebouncePending = false;
    InvalidateWindowPreviewOverlay();
  }

  private void ResetSnapshotPipelineState()
  {
    _invalidSnapshotAttempts = 0;
    _windowBoundsDebounce.Stop();
    _windowBoundsDebouncePending = false;
    _windowPreviewRebuildScheduled = false;
  }

  private WindowPreviewSnapshot CaptureWindowPreviewSnapshot()
  {
    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; skipping snapshot.");
      return _windowPreviewSnapshot;
    }

    if (preview.IsPreviewRunning)
    {
      _logger.Debug("CaptureWindowPreviewSnapshot skipped_due_to_live_preview");

      _windowBoundsDebounce?.Stop();
      _windowBoundsDebouncePending = false;
      _windowPreviewRebuildScheduled = false;
      return _windowPreviewSnapshot;
    }

    using var logicalDepth = new MonitorPreviewDisplay.PreviewLogicalScope(nameof(CaptureWindowPreviewSnapshot), _logger);
    if (!logicalDepth.Entered)
    {
      _logger.Error("CaptureWindowPreviewSnapshot: preview logical depth limit reached; aborting snapshot");
      return _windowPreviewSnapshot;
    }

    using var depth = new MonitorPreviewDisplay.PreviewCallScope(nameof(CaptureWindowPreviewSnapshot), _logger);
    if (!depth.Entered)
    {
      _logger.Error("CaptureWindowPreviewSnapshot: depth limit reached; aborting snapshot");
      return _windowPreviewSnapshot;
    }

    var now = _windowPreviewStopwatch.Elapsed;
    var monitor = GetSelectedMonitor();
    var monitorId = monitor is null ? null : MonitorIdentifier.Create(monitor);
    var monitorBounds = ResolveMonitorBounds(monitor);

    if (monitorId is not null && (monitorBounds.Width < MinimumOverlayDimension || monitorBounds.Height < MinimumOverlayDimension))
    {
      _logger.Warning(
          "CaptureWindowPreviewSnapshot: monitor sem superfície válida width={Width} height={Height} monitorId={MonitorId}",
          monitorBounds.Width,
          monitorBounds.Height,
          monitorId);
      monitorId = null;
    }

    var autoStart = chkAutoStart?.Checked ?? false;
    var isFullScreen = chkJanelaTelaCheia?.Checked ?? false;
    var appId = txtId?.Text?.Trim() ?? string.Empty;

    var bounds = Drawing.Rectangle.Empty;
    if (nudJanelaX is not null &&
        nudJanelaY is not null &&
        nudJanelaLargura is not null &&
        nudJanelaAltura is not null)
    {
      bounds = new Drawing.Rectangle(
          (int)nudJanelaX.Value,
          (int)nudJanelaY.Value,
          (int)nudJanelaLargura.Value,
          (int)nudJanelaAltura.Value);
    }

    if (bounds.Width < MinimumOverlayDimension || bounds.Height < MinimumOverlayDimension)
    {
      _invalidSnapshotAttempts++;
      _logger.Debug(
          "snapshot_invalid_bounds bounds={Bounds} attempts={Attempts}",
          bounds,
          _invalidSnapshotAttempts);

      if (_invalidSnapshotAttempts >= 5)
      {
        _windowBoundsDebounce.Stop();
        _windowBoundsDebouncePending = false;
        _windowPreviewRebuildScheduled = false;
        _logger.Warning(
            "snapshot_invalid_bounds_limit_reached; disabling snapshot pipeline until reset.");
      }

      return _windowPreviewSnapshot;
    }

    _invalidSnapshotAttempts = 0;

    var currentPoint = bounds.Location;
    if (_hasWindowPreviewSnapshot)
    {
      var elapsed = now - _lastWindowPreviewSnapshotAt;
      var lastPoint = _lastWindowPreviewSnapshotPoint;
      if (elapsed < WindowPreviewSnapshotThrottleInterval &&
          lastPoint.HasValue &&
          Math.Abs(currentPoint.X - lastPoint.Value.X) < WindowPreviewSnapshotDelta &&
          Math.Abs(currentPoint.Y - lastPoint.Value.Y) < WindowPreviewSnapshotDelta)
      {
        _logger.Debug(
            "CaptureWindowPreviewSnapshot: throttled elapsed={Elapsed} delta=({DeltaX},{DeltaY})",
            elapsed,
            currentPoint.X - lastPoint.Value.X,
            currentPoint.Y - lastPoint.Value.Y);

        return _windowPreviewSnapshot;
      }
    }

    var snapshot = new WindowPreviewSnapshot(monitorId, bounds, isFullScreen, autoStart, appId);
    _lastWindowPreviewSnapshotAt = now;
    _lastWindowPreviewSnapshotPoint = currentPoint;

    _logger.Debug(
        "CaptureWindowPreviewSnapshot: exit bounds={Bounds} monitorId={MonitorId} fullScreen={FullScreen} autoStart={AutoStart} appId={AppId}",
        snapshot.Bounds,
        snapshot.MonitorId ?? string.Empty,
        snapshot.IsFullScreen,
        snapshot.AutoStart,
        snapshot.AppId);

    return snapshot;
  }

  private Drawing.Rectangle ResolveMonitorBounds(MonitorInfo? monitor)
  {
    if (monitor is null)
    {
      return Drawing.Rectangle.Empty;
    }

    if (monitor.Bounds.Width >= MinimumOverlayDimension && monitor.Bounds.Height >= MinimumOverlayDimension)
    {
      return monitor.Bounds;
    }

    if (TryGetDisplaySettings(monitor.DeviceName, out var rect))
    {
      _logger.Debug(
          "CaptureWindowPreviewSnapshot: recalculou bounds via EnumDisplaySettings width={Width} height={Height} monitor={MonitorId}",
          rect.Width,
          rect.Height,
          monitor.DeviceName);
      return rect;
    }

    try
    {
      foreach (var screen in WinForms.Screen.AllScreens)
      {
        if (string.Equals(screen.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase) &&
            screen.Bounds.Width >= MinimumOverlayDimension &&
            screen.Bounds.Height >= MinimumOverlayDimension)
        {
          return screen.Bounds;
        }
      }
    }
    catch (Exception ex)
    {
      _logger.Debug(ex, "Falha ao consultar Screen.AllScreens para bounds.");
    }

    _logger.Debug(
        "invalid_bounds_detected width={Width} height={Height} monitorId={MonitorId} source=capture_snapshot",
        monitor.Bounds.Width,
        monitor.Bounds.Height,
        monitor.DeviceName ?? string.Empty);
    return Drawing.Rectangle.Empty;
  }

  private static bool TryGetDisplaySettings(string? deviceName, out Drawing.Rectangle bounds)
  {
    bounds = Drawing.Rectangle.Empty;

    if (!OperatingSystem.IsWindows())
    {
      return false;
    }

    if (string.IsNullOrWhiteSpace(deviceName))
    {
      return false;
    }

    try
    {
      var mode = new DEVMODE
      {
        dmSize = (short)Marshal.SizeOf<DEVMODE>(),
      };

      if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
      {
        return false;
      }

      bounds = new Drawing.Rectangle(mode.dmPositionX, mode.dmPositionY, mode.dmPelsWidth, mode.dmPelsHeight);
      return bounds.Width >= MinimumOverlayDimension && bounds.Height >= MinimumOverlayDimension;
    }
    catch (DllNotFoundException)
    {
      return false;
    }
    catch (EntryPointNotFoundException)
    {
      return false;
    }
  }

  private void ApplyWindowPreviewSnapshot(WindowPreviewSnapshot snapshot, TimeSpan timestamp)
  {
    _windowPreviewSnapshot = snapshot;
    _hasWindowPreviewSnapshot = true;
    _lastWindowPreviewRebuild = timestamp;
    _windowPreviewRebuildScheduled = false;

    if (snapshot.Bounds.Width < MinimumOverlayDimension || snapshot.Bounds.Height < MinimumOverlayDimension)
    {
      _logger.Debug(
          "ApplySelectionOverlay: ignored_zero_bound_overlay bounds={Bounds} monitor={MonitorId}",
          snapshot.Bounds,
          snapshot.MonitorId ?? string.Empty);
      return;
    }

    if (ShouldApplyOverlay(snapshot))
    {
      CacheOverlaySnapshot(snapshot);
      RebuildSimulationOverlays();
      _logger.Debug(
          "ApplySelectionOverlay: bounds={Bounds} monitor={MonitorId} fullScreen={FullScreen} autoStart={AutoStart}",
          snapshot.Bounds,
          snapshot.MonitorId ?? string.Empty,
          snapshot.IsFullScreen,
          snapshot.AutoStart);
      monitorPreviewDisplay?.Invalidate();
    }
  }

  private async void ScheduleWindowPreviewRebuild(TimeSpan delay)
  {
    try
    {
      var preview = TryGetPreviewControl();
      if (preview == null)
      {
        _logger.Debug("Preview control null; skipping window/overlay update.");
        _windowPreviewRebuildScheduled = false;
        return;
      }

      if (preview.IsDisposed)
      {
        _windowPreviewRebuildScheduled = false;
        return;
      }

      if (preview.IsPreviewRunning)
      {
        _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

        _windowBoundsDebounce?.Stop();
        _windowBoundsDebouncePending = false;
        _windowPreviewRebuildScheduled = false;
        return;
      }

      try
      {
        if (delay < TimeSpan.Zero)
        {
          delay = TimeSpan.Zero;
        }

        await Task.Delay(delay).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
        _logger.Debug(ex, "Falha no atraso de ScheduleWindowPreviewRebuild.");
        _windowPreviewRebuildScheduled = false;
        return;
      }

      if (IsDisposed)
      {
        _windowPreviewRebuildScheduled = false;
        return;
      }

      if (preview.IsPreviewRunning)
      {
        _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

        _windowBoundsDebounce?.Stop();
        _windowBoundsDebouncePending = false;
        _windowPreviewRebuildScheduled = false;
        return;
      }

      _windowPreviewRebuildScheduled = false;
      InvalidateWindowPreviewOverlay();
    }
    catch (Exception ex)
    {
      _windowPreviewRebuildScheduled = false;
      _logger.Warning(ex, "Falha não tratada em ScheduleWindowPreviewRebuild.");
    }
  }
}
