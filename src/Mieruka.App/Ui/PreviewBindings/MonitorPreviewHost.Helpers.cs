#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.Core.Config;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Preview;
using Serilog;

namespace Mieruka.App.Ui.PreviewBindings;

public sealed partial class MonitorPreviewHost
{
    internal static string CreateMonitorId(MonitorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var hasAdapter = descriptor.AdapterLuidHi != 0 || descriptor.AdapterLuidLo != 0 || descriptor.TargetId != 0;
        if (!hasAdapter)
        {
            return descriptor.DeviceName ?? string.Empty;
        }

        var key = new MonitorKey
        {
            AdapterLuidHigh = (int)descriptor.AdapterLuidHi,
            AdapterLuidLow = (int)descriptor.AdapterLuidLo,
            TargetId = unchecked((int)descriptor.TargetId),
            DeviceId = descriptor.DeviceName ?? string.Empty,
        };

        return MonitorIdentifier.Create(key, descriptor.DeviceName);
    }

    private string GetBackendLabel()
    {
        lock (_stateGate)
        {
            if (!_hasActiveSession)
            {
                return "Idle";
            }

            return _isGpuActive ? "GPU" : "GDI";
        }
    }

    private ILogger ForQueueEvent(string eventId)
        => ForEvent(eventId)
            .ForContext("PreviewSessionId", _previewSessionId)
            .ForContext("MonitorId", MonitorId)
            .ForContext("Backend", GetBackendLabel());

    private ILogger ForEvent(string eventId)
        => _logger.ForContext("EventId", eventId);

    private void LogReentrancyBlocked(string operation)
    {
        if (_suppressEvents)
        {
            return;
        }

        Interlocked.Increment(ref _reentrancyBlockedCount);
        MaybeReportGateBlocks();

        ForEvent("ReentrancyBlocked").Debug("Operação {Operation} bloqueada por reentrância.", operation);
    }

    private static string GetMonitorFriendlyName(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.Name))
        {
            return monitor.Name;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            return monitor.DeviceName;
        }

        return MonitorIdentifier.Create(monitor);
    }

    private void LogGpuFallback(string monitorKey, string monitorFriendlyName)
    {
        if (_suppressEvents)
        {
            return;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        var previous = Volatile.Read(ref _lastGpuFallbackLogTicks);
        if (previous != 0)
        {
            var elapsed = nowTicks - previous;
            if (elapsed >= 0 && elapsed < GpuFallbackLogInterval.Ticks)
            {
                return;
            }
        }

        Interlocked.Exchange(ref _lastGpuFallbackLogTicks, nowTicks);
        _logger.Information(
            "GpuCaptureFallbackAtivado monitor={MonitorFriendly} key={MonitorKey}",
            monitorFriendlyName,
            monitorKey);
    }

    private void EnsurePictureBoxSizeMode()
    {
        if (_target.SizeMode != WinForms.PictureBoxSizeMode.Zoom)
        {
            _target.SizeMode = WinForms.PictureBoxSizeMode.Zoom;
        }

        EnableDoubleBuffering(_target);
    }

    private bool HasUsableTargetArea()
    {
        if (_target.IsDisposed)
        {
            return false;
        }

        try
        {
            var width = _target.Width;
            var height = _target.Height;
            return width > 0 && height > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static void EnableDoubleBuffering(WinForms.PictureBox target)
    {
        try
        {
            typeof(WinForms.Control)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?
                .SetValue(target, true);
        }
        catch
        {
            // Ignore failures when enabling double buffering; the preview will continue without it.
        }
    }

    private bool PopulateMetadataFromMonitor()
    {
        if (_monitorBounds != Drawing.Rectangle.Empty)
        {
            return true;
        }

        MonitorInfo? info = null;
        try
        {
            info = MonitorLocator.Find(MonitorId);
        }
        catch
        {
            // Metadata retrieval is best-effort only.
        }

        if (info is not null)
        {
            if (info.Bounds != Drawing.Rectangle.Empty)
            {
                _monitorBounds = info.Bounds;
            }

            if (info.WorkArea != Drawing.Rectangle.Empty)
            {
                _monitorWorkArea = info.WorkArea;
            }

            if (info.Orientation != MonitorOrientation.Unknown)
            {
                _orientation = info.Orientation;
            }

            if (info.Rotation != 0)
            {
                _rotation = info.Rotation;
            }

            if (_monitorBounds == Drawing.Rectangle.Empty && info.Width > 0 && info.Height > 0)
            {
                _monitorBounds = new Drawing.Rectangle(0, 0, info.Width, info.Height);
            }

            if (_monitorWorkArea == Drawing.Rectangle.Empty && _monitorBounds != Drawing.Rectangle.Empty)
            {
                _monitorWorkArea = _monitorBounds;
            }

            if (_monitorBounds != Drawing.Rectangle.Empty)
            {
                return true;
            }
        }

        return TryPopulateMetadataFallback();
    }

    private bool TryPopulateMetadataFallback()
    {
        try
        {
            var service = new MonitorService();
            var fallback = service.PrimaryOrFirst();

            if (fallback.Bounds != Drawing.Rectangle.Empty)
            {
                _monitorBounds = fallback.Bounds;
            }
            else if (fallback.Width > 0 && fallback.Height > 0)
            {
                _monitorBounds = new Drawing.Rectangle(0, 0, fallback.Width, fallback.Height);
            }

            if (fallback.WorkArea != Drawing.Rectangle.Empty)
            {
                _monitorWorkArea = fallback.WorkArea;
            }
            else if (_monitorBounds != Drawing.Rectangle.Empty)
            {
                _monitorWorkArea = _monitorBounds;
            }

            if (fallback.Orientation != MonitorOrientation.Unknown)
            {
                _orientation = fallback.Orientation;
            }

            if (fallback.Rotation != 0)
            {
                _rotation = fallback.Rotation;
            }

            if (fallback.RefreshHz > 0)
            {
                _refreshRate = fallback.RefreshHz;
            }

            return _monitorBounds != Drawing.Rectangle.Empty;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

#if DEBUG
    private void DrawDebugOverlay(Drawing.Bitmap bitmap)
    {
        try
        {
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            using var font = Drawing.SystemFonts.CaptionFont ?? Drawing.SystemFonts.MessageBoxFont ?? WinForms.Control.DefaultFont;
            using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(160, Drawing.Color.Black));
            using var foreground = new Drawing.SolidBrush(Drawing.Color.White);

            var text = string.Concat(MonitorId, " ", bitmap.Width, "x", bitmap.Height);
            if (_refreshRate > 0)
            {
                text = string.Concat(text, " @", _refreshRate, "Hz");
            }

            if (_rotation != 0)
            {
                text = string.Concat(text, " rot:", _rotation);
            }
            else if (_orientation != MonitorOrientation.Unknown)
            {
                text = string.Concat(text, " ", _orientation);
            }

            var safeFont = font ?? WinForms.Control.DefaultFont;
            var safeText = text ?? string.Empty;
            var measured = graphics.MeasureString(safeText, safeFont);
            var rect = new Drawing.RectangleF(4, 4, measured.Width + 8, measured.Height + 4);
            graphics.FillRectangle(background, rect);
            graphics.DrawString(safeText, safeFont, foreground, new Drawing.PointF(rect.Left + 4, rect.Top + 2));
        }
        catch
        {
            // Debug overlay is best-effort only.
        }
    }
#endif

    private readonly struct StartStopScope : IDisposable
    {
        private readonly MonitorPreviewHost? _owner;

        public StartStopScope(MonitorPreviewHost owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.ExitStartStop();
        }
    }

    private readonly struct BusyScope : IDisposable
    {
        private readonly MonitorPreviewHost? _owner;

        public BusyScope(MonitorPreviewHost owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.ExitBusy();
        }
    }
}
