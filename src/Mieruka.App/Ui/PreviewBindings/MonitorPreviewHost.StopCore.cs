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
    private async Task StopCoreAsync(bool clearFrame, bool resetPaused, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StopCoreAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (ReadState() == PreviewState.Disposing)
        {
            return;
        }

        var targetState = clearFrame ? PreviewState.Stopped : PreviewState.Paused;
        PreviewState previousState;

        ReadOnlySpan<PreviewState> candidates = clearFrame
            ? new[] { PreviewState.Running, PreviewState.Starting, PreviewState.Paused }
            : new[] { PreviewState.Running, PreviewState.Starting };

        if (!TryTransition(candidates, PreviewState.Pausing, out previousState))
        {
            if (ReadState() == targetState)
            {
                if (clearFrame)
                {
                    ClearFrame();
                }

                return;
            }

            if (!clearFrame && ReadState() == PreviewState.Pausing)
            {
                PostStop(clearFrame, resetPaused);
            }

            return;
        }

        if (!TryEnterStartStop(nameof(StopAsync), () => PostStop(clearFrame, resetPaused), out var scope))
        {
            SetState(previousState);
            return;
        }

        using (scope)
        {
            await StopCoreUnsafeAsync(clearFrame, resetPaused, cancellationToken).ConfigureAwait(false);
            SetState(targetState);
        }
    }

    private async Task StopCoreUnsafeAsync(bool clearFrame, bool resetPaused, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StopCoreUnsafeAsync));
        if (!guard.Entered)
        {
            return;
        }

        await StopExternalAsync().ConfigureAwait(false);

        IMonitorCapture? capture;
        lock (_gate)
        {
            capture = Capture;
            Capture = null;
        }

        if (capture is not null)
        {
            capture.FrameArrived -= OnFrameArrived;
            await SafeDisposeAsync(capture, cancellationToken).ConfigureAwait(false);
            if (!_suppressEvents)
            {
                _logger.Information("CaptureDisposed");
            }
        }

        DisposePendingFrames();

        if (resetPaused)
        {
            Interlocked.Exchange(ref _paused, 0);
            Volatile.Write(ref _previewPaused, false);
        }

        if (clearFrame)
        {
            ClearFrame();
        }
        else
        {
            ResetFrameThrottle();
        }

        Volatile.Write(ref _lifecycleState, LifecycleStopped);
        Volatile.Write(ref _previewRunning, false);
    }

    private async Task<bool> DisposeCaptureRetainingFrameAsync(bool resetPaused, CancellationToken cancellationToken)
    {
        bool hasCapture;
        lock (_gate)
        {
            hasCapture = Capture is not null;
        }

        if (!hasCapture)
        {
            if (resetPaused)
            {
                Interlocked.Exchange(ref _paused, 0);
            }

            return false;
        }

        await StopCoreAsync(clearFrame: false, resetPaused: resetPaused, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void PostStart(bool preferGpu)
    {
        using var guard = new StackGuard(nameof(PostStart));
        if (!guard.Entered)
        {
            return;
        }

        if (_suppressEvents || _disposed || _target.IsDisposed)
        {
            return;
        }

        try
        {
            _target.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await StartCoreAsync(preferGpu, CancellationToken.None).ConfigureAwait(true);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void PostStop(bool clearFrame, bool resetPaused)
    {
        using var guard = new StackGuard(nameof(PostStop));
        if (!guard.Entered)
        {
            return;
        }

        if (_suppressEvents || _target.IsDisposed)
        {
            return;
        }

        try
        {
            _target.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await StopCoreAsync(clearFrame, resetPaused, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
