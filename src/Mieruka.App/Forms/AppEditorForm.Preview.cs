#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Interop;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms;

public partial class AppEditorForm
{
  private void TabEditor_SelectedIndexChanged(object? sender, EventArgs e)
  {
    try
    {
      UpdatePreviewVisibility();
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Falha não tratada em TabEditor_SelectedIndexChanged.");
    }
  }

  private async void cboMonitores_SelectedIndexChanged(object? sender, EventArgs e)
  {
    if (_suppressMonitorComboEvents)
    {
      return;
    }

    await UpdateMonitorPreviewSafelyAsync().ConfigureAwait(true);
  }

  private async Task UpdateMonitorPreviewSafelyAsync()
  {
    try
    {
      await UpdateMonitorPreviewAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "UpdateMonitorPreviewSafelyAsync: monitor preview update failed");
    }
  }

  private async Task UpdateMonitorPreviewAsync()
  {
    await _monitorPreviewGate.WaitAsync().ConfigureAwait(true);
    try
    {
      var previousMonitor = _selectedMonitorInfo;
      var previousMonitorId = _selectedMonitorId;
      var previousWindow = BuildWindowConfigurationFromInputs();

      _selectedMonitorInfo = null;
      _selectedMonitorId = null;

      if (cboMonitores?.SelectedItem is not MonitorOption option || option.Monitor is null || option.MonitorId is null)
      {
        if (monitorPreviewDisplay is not null)
        {
          await monitorPreviewDisplay.UnbindAsync().ConfigureAwait(true);
        }

        _monitorPreviewMonitorId = null;
        UpdateMonitorCoordinateLabel(null);
        monitorPreviewDisplay?.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
        return;
      }

      _selectedMonitorInfo = option.Monitor;
      _selectedMonitorId = option.MonitorId;

      var monitorChanged = !string.Equals(_monitorPreviewMonitorId, option.MonitorId, StringComparison.OrdinalIgnoreCase);

      if (previousMonitor is not null &&
          !string.Equals(previousMonitorId, option.MonitorId, StringComparison.OrdinalIgnoreCase))
      {
        ApplyRelativeWindowToNewMonitor(previousWindow, previousMonitor, option.Monitor);
      }

      if (monitorChanged && monitorPreviewDisplay is not null)
      {
        ResetSnapshotPipelineState();
        await monitorPreviewDisplay.BindAsync(option.Monitor, autoStart: true).ConfigureAwait(true);
        _monitorPreviewMonitorId = option.MonitorId;
      }

      UpdateMonitorCoordinateLabel(null);
      RebuildSimulationOverlays();
    }
    finally
    {
      _monitorPreviewGate.Release();
    }
  }

  private void chkJanelaTelaCheia_CheckedChanged(object? sender, EventArgs e)
  {
    UpdateWindowInputsState();

    _ = ClampWindowInputsToMonitor(null, allowFullScreen: true);
    QueueWindowOverlayUpdate();
  }

  private void UpdateWindowInputsState()
  {
    var enabled = !chkJanelaTelaCheia.Checked;

    if (nudJanelaX is not null)
    {
      nudJanelaX.Enabled = enabled;
    }

    if (nudJanelaY is not null)
    {
      nudJanelaY.Enabled = enabled;
    }

    if (nudJanelaLargura is not null)
    {
      nudJanelaLargura.Enabled = enabled;
    }

    if (nudJanelaAltura is not null)
    {
      nudJanelaAltura.Enabled = enabled;
    }
  }

  private void UpdateMonitorCoordinateLabel(Drawing.PointF? coordinates)
  {
    if (lblMonitorCoordinates is not { IsDisposed: false } label)
    {
      return;
    }

    var text = coordinates is null
        ? "X=-, Y=-"
        : $"X={coordinates.Value.X}, Y={coordinates.Value.Y}";

    if (!string.Equals(label.Text, text, StringComparison.Ordinal))
    {
      label.Text = text;
    }
  }

  private MonitorPreviewDisplay? TryGetPreviewControl()
  {
    return monitorPreviewDisplay;
  }

  private void MonitorPreviewDisplay_OnMonitorMouseMove(object? sender, MonitorPreviewDisplay.MonitorMouseEventArgs e)
  {
    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; ignoring mouse interaction.");
      return;
    }

    if (preview.IsInteractionSuppressed)
    {
      return;
    }

    UpdateMonitorCoordinateLabel(e.MonitorPoint);
  }

  private void MonitorPreviewDisplay_OnMonitorMouseClick(object? sender, MonitorPreviewDisplay.MonitorMouseEventArgs e)
  {
    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; ignoring mouse interaction.");
      return;
    }

    if (preview.IsInteractionSuppressed)
    {
      return;
    }

    UpdateMonitorCoordinateLabel(e.MonitorPoint);
    _lastClickMonitorPoint = e.MonitorPoint;

    if (e.MonitorPoint is { } clickPt && !chkJanelaTelaCheia.Checked)
    {
      ShowCoordinateInputDialog(clickPt);
    }
  }

  private void ShowCoordinateInputDialog(Drawing.PointF clickPt)
  {
    if (!TryGetWindowInputs(out var xInput, out var yInput, out var widthInput, out var heightInput))
    {
      return;
    }

    var monitor = GetSelectedMonitor();
    if (monitor is null)
    {
      return;
    }

    var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
    var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
    var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

    var clickX = (int)Math.Round(clickPt.X);
    var clickY = (int)Math.Round(clickPt.Y);
    var currentWidth = (int)widthInput.Value;
    var currentHeight = (int)heightInput.Value;

    using var dialog = new CoordinateInputDialog(
        clickX, clickY,
        currentWidth, currentHeight,
        monitorWidth, monitorHeight);

    if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
    {
      return;
    }

    _suppressWindowInputHandlers = true;
    try
    {
      widthInput.Value = dialog.SelectedWidth;
      heightInput.Value = dialog.SelectedHeight;
      xInput.Value = dialog.SelectedX;
      yInput.Value = dialog.SelectedY;
    }
    finally
    {
      _suppressWindowInputHandlers = false;
    }

    InvalidateWindowPreviewOverlay();
  }

  private void MonitorPreviewDisplay_MonitorMouseLeft(object? sender, EventArgs e)
  {
    var preview = TryGetPreviewControl();
    if (preview == null)
    {
      _logger.Debug("Preview control null; ignoring mouse interaction.");
      return;
    }

    UpdateMonitorCoordinateLabel(null);

    _hoverPendingPoint = null;
    _hoverAppliedPoint = null;
    _hoverSw.Reset();
    CancelHoverThrottleTimer();

    if (preview.IsPreviewRunning)
    {
      _logger.Debug("Action skipped because live preview is running.");

      _windowBoundsDebounce?.Stop();
      _windowBoundsDebouncePending = false;
      _windowPreviewRebuildScheduled = false;

      return;
    }

    _ = ClampWindowInputsToMonitor(null);
    InvalidateWindowPreviewOverlay();
  }

  private void MonitorPreviewDisplay_OnSimRectMoved(object? sender, MonitorPreviewDisplay.SimRectMovedEventArgs e)
  {
    if (chkJanelaTelaCheia.Checked)
    {
      return;
    }

    _suppressWindowInputHandlers = true;
    try
    {
      var bounds = e.MonitorBounds;
      nudJanelaX.Value = AjustarRange(nudJanelaX, bounds.X);
      nudJanelaY.Value = AjustarRange(nudJanelaY, bounds.Y);
      nudJanelaLargura.Value = AjustarRange(nudJanelaLargura, bounds.Width);
      nudJanelaAltura.Value = AjustarRange(nudJanelaAltura, bounds.Height);
    }
    finally
    {
      _suppressWindowInputHandlers = false;
    }

    InvalidateWindowPreviewOverlay();
  }

  private void ScheduleHoverPointUpdate(TimeSpan delay)
  {
    if (IsDisposed)
    {
      return;
    }

    if (delay < TimeSpan.Zero)
    {
      delay = TimeSpan.Zero;
    }

    CancelHoverThrottleTimer();

    var source = new CancellationTokenSource();
    _hoverThrottleCts = source;
    _ = FlushHoverPointAsync(delay, source.Token);
  }

  private async Task FlushHoverPointAsync(TimeSpan delay, CancellationToken token)
  {
    try
    {
      if (delay > TimeSpan.Zero)
      {
        await Task.Delay(delay, token).ConfigureAwait(false);
      }
    }
    catch (TaskCanceledException)
    {
      return;
    }
    catch (ObjectDisposedException)
    {
      return;
    }
    catch (Exception ex)
    {
      _logger.Debug(ex, "Falha inesperada durante atraso de preview.");
      return;
    }

    if (token.IsCancellationRequested)
    {
      return;
    }

    MarshalToUi(() =>
    {
      if (token.IsCancellationRequested || IsDisposed)
      {
        return;
      }

      CancelHoverThrottleTimer();
      ApplyPendingHoverPoint(enforceInterval: true);
    });
  }

  private void ApplyPendingHoverPoint(bool enforceInterval)
  {
    if (!IsOnUiThread())
    {
      MarshalToUi(() => ApplyPendingHoverPoint(enforceInterval));
      return;
    }

    var preview = monitorPreviewDisplay;
    if (preview is null || preview.IsDisposed || preview.IsInteractionSuppressed)
    {
      return;
    }

    if (_hoverPendingPoint is not Drawing.Point pending)
    {
      return;
    }

    if (!TryGetWindowInputs(out var xInput, out var yInput, out var widthInput, out var heightInput))
    {
      return;
    }

    if (!_hoverSw.IsRunning)
    {
      _hoverSw.Start();
    }

    if (enforceInterval && _hoverSw.Elapsed < HoverThrottleInterval)
    {
      var remaining = HoverThrottleInterval - _hoverSw.Elapsed;
      ScheduleHoverPointUpdate(remaining);
      return;
    }

    if (_hoverAppliedPoint is Drawing.Point applied && applied == pending)
    {
      _hoverSw.Restart();
      return;
    }

    _hoverSw.Restart();
    _hoverAppliedPoint = pending;
    _ = ClampWindowInputsToMonitor(pending, windowInputs: (xInput, yInput, widthInput, heightInput));
  }

  private void MarshalToUi(Action action)
  {
    if (action is null || IsDisposed)
    {
      return;
    }

    void InvokeSafely()
    {
      if (IsDisposed)
      {
        return;
      }

      try
      {
        action();
      }
      catch (ObjectDisposedException)
      {
        // Ignore callbacks that race with disposal.
      }
      catch (InvalidOperationException ex) when (IsDisposed || !IsHandleCreated)
      {
        _logger.Debug(ex, "Callback ignorado devido ao controle estar indisponível.");
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Falha ao processar callback de UI agendado.");
      }
    }

    var callback = new WinForms.MethodInvoker(InvokeSafely);

    if (!IsOnUiThread())
    {
      var context = _uiContext;
      if (context is not null)
      {
        try
        {
          context.Post(static state =>
          {
            if (state is WinForms.MethodInvoker invoker)
            {
              invoker();
            }
          }, callback);
        }
        catch (ObjectDisposedException)
        {
          // Ignore context disposal during shutdown.
        }
        catch (InvalidOperationException)
        {
          // Ignore marshaling failures when the context is no longer available.
        }

        return;
      }

      try
      {
        BeginInvoke(callback);
      }
      catch (ObjectDisposedException)
      {
        // Ignore marshaling failures after disposal.
      }
      catch (InvalidOperationException)
      {
        // Ignore marshaling failures when the window handle is gone.
      }

      return;
    }

    InvokeSafely();
  }

  private bool IsOnUiThread()
  {
    return Environment.CurrentManagedThreadId == _uiThreadId;
  }

  private void CancelHoverThrottleTimer()
  {
    var pending = _hoverThrottleCts;
    if (pending is null)
    {
      return;
    }

    _hoverThrottleCts = null;

    try
    {
      pending.Cancel();
    }
    catch (ObjectDisposedException)
    {
      // Ignorar cancelamentos após descarte.
    }
    catch (AggregateException)
    {
      // Ignorar cancelamentos concorrentes.
    }

    pending.Dispose();
  }
}
