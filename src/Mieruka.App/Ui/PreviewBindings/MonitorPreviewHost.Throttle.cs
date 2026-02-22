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
    private bool ShouldProcessFrame()
    {
        if (IsEditorSnapshotActive)
        {
            return false;
        }

        if (IsEditorPreviewDisabled)
        {
            return false;
        }

        if (!IsVisible)
        {
            return false;
        }

        // Lock-free frame timing using Interlocked.CompareExchange on ticks.
        // This eliminates lock contention on the hot path (every frame arrival).
        var throttleTicks = Volatile.Read(ref _frameThrottleTicks);
        if (throttleTicks <= 0)
        {
            return true;
        }

        var nowTicks = DateTime.UtcNow.Ticks;
        while (true)
        {
            var deadline = Volatile.Read(ref _nextFrameAtTicks);
            if (nowTicks < deadline)
            {
                return false;
            }

            var newDeadline = nowTicks + throttleTicks;
            if (Interlocked.CompareExchange(ref _nextFrameAtTicks, newDeadline, deadline) == deadline)
            {
                return true;
            }

            // Another thread won the race; re-check.
        }
    }

    private bool HasPendingFrameCapacity()
    {
        int pendingCount;
        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count < PendingFrameLimit)
            {
                return true;
            }

            pendingCount = _pendingFrames.Count;
        }

        ForQueueEvent("FrameDroppedBackpressure").Debug(
            "Limite de fila atingido; descartando frame sem bloquear UI. Pendentes={Pending} Limite={Limit}",
            pendingCount,
            PendingFrameLimit);
        return false;
    }

    private void ResetFrameThrottle()
    {
        Volatile.Write(ref _nextFrameAtTicks, 0L);
    }

    private void UpdateFrameThrottleForCapture(IMonitorCapture capture)
    {
        lock (_frameTimingGate)
        {
            if (_frameThrottleCustomized)
            {
                return;
            }

            if (_safeModeEnabled)
            {
                _frameThrottle = SafeModeFrameThrottle;
                Volatile.Write(ref _frameThrottleTicks, SafeModeFrameThrottle.Ticks);
                Volatile.Write(ref _nextFrameAtTicks, 0L);
                return;
            }

            var targetThrottle = capture is GraphicsCaptureProvider ? GpuFrameThrottle : GdiFrameThrottle;
            _frameThrottle = targetThrottle;
            Volatile.Write(ref _frameThrottleTicks, targetThrottle.Ticks);
            if (_frameThrottle <= TimeSpan.Zero)
            {
                Volatile.Write(ref _nextFrameAtTicks, 0L);
            }
        }
    }

    private bool ShouldDisplayFrame()
    {
        if (IsEditorSnapshotActive)
        {
            return false;
        }

        if (IsEditorPreviewDisabled)
        {
            return false;
        }

        if (Volatile.Read(ref _paused) == 1 || _isSuspended || !IsVisible)
        {
            return false;
        }

        lock (_stateGate)
        {
            return _hasActiveSession;
        }
    }

    private bool RegisterPendingFrame(Drawing.Bitmap frame, out int pendingCount)
    {
        lock (_pendingFramesGate)
        {
            // Drop oldest frames when the queue is full instead of rejecting
            // the newest frame. This keeps the displayed image as fresh as
            // possible and prevents visible stutter during brief UI delays.
            while (_pendingFrames.Count >= PendingFrameLimit)
            {
                var oldest = _pendingFrames[0];
                _pendingFrames.RemoveAt(0);
                try
                {
                    oldest.Dispose();
                }
                catch
                {
                    // Ignore failures while disposing stale frames.
                }
            }

            _pendingFrames.Add(frame);
            _hasPendingFrame = true;
            pendingCount = _pendingFrames.Count;
        }

        return true;
    }

    private void UnregisterPendingFrame(Drawing.Bitmap frame)
    {
        lock (_pendingFramesGate)
        {
            var previousCount = _pendingFrames.Count;
            _pendingFrames.Remove(frame);
            if (_pendingFrames.Count == 0)
            {
                _hasPendingFrame = false;
                if (previousCount > 0)
                {
                    ForQueueEvent("FrameQueueIdle").Debug(
                        "Fila de frames vazia; preview ocioso. Sessao={Session} Visivel={IsVisibleState}",
                        _previewSessionId,
                        IsVisible);
                }
            }
        }
    }

    private void DisposePendingFrames()
    {
        Drawing.Bitmap[]? frames = null;

        lock (_pendingFramesGate)
        {
            if (_pendingFrames.Count > 0)
            {
                frames = _pendingFrames.ToArray();
                _pendingFrames.Clear();
                _hasPendingFrame = false;
            }
        }

        if (frames is null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            try
            {
                frame.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Falha ao descartar frame pendente durante limpeza.");
            }
        }
    }

    private static async ValueTask SafeDisposeAsync(IMonitorCapture capture, CancellationToken cancellationToken)
    {
        try
        {
            var stopTask = capture.StopAsync();
            if (!stopTask.IsCompletedSuccessfully)
            {
                await stopTask.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                stopTask.GetAwaiter().GetResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StaticLogger.Debug(ex, "Falha ao parar captura durante descarte seguro.");
        }

        try
        {
            var disposeTask = capture.DisposeAsync().AsTask();
            if (!disposeTask.IsCompletedSuccessfully)
            {
                await disposeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await disposeTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StaticLogger.Debug(ex, "Falha ao descartar captura durante descarte seguro.");
        }
    }
}
