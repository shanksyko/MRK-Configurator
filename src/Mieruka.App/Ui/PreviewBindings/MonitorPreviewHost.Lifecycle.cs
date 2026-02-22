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
    public async Task<bool> StartAsync(bool preferGpu, CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(StartAsync));
        if (!guard.Entered)
        {
            return false;
        }

        if (!_lifecycleGate.TryEnter())
        {
            LogReentrancyBlocked(nameof(StartAsync));
            return Capture is not null;
        }

        try
        {
            var preferGpuAdjusted = preferGpu && !PreviewSafeModeEnabled;

            if (!TryEnterBusy(out var scope))
            {
                LogReentrancyBlocked(nameof(StartAsync));
                return Capture is not null;
            }

            try
            {
                return await StartCoreAsync(preferGpuAdjusted, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Exit();
        }
    }

    /// <summary>
    /// Stops the current preview session.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(StopAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (!_lifecycleGate.TryEnter())
        {
            LogReentrancyBlocked(nameof(StopAsync));
            return;
        }

        try
        {
            if (!TryEnterBusy(out var scope))
            {
                LogReentrancyBlocked(nameof(StopAsync));
                return;
            }

            try
            {
                lock (_stateGate)
                {
                    _hasActiveSession = false;
                    _isGpuActive = false;
                    _isSuspended = false;
                }

                Interlocked.Exchange(ref _paused, 0);
                await StopCoreAsync(clearFrame: true, resetPaused: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Dispose();
            }
        }
        finally
        {
            _lifecycleGate.Exit();
        }
    }

    /// <summary>
    /// Stops the current preview session while preventing overlapping lifecycle transitions.
    /// </summary>
    public async Task StopSafeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = Volatile.Read(ref _lifecycleState);

            if (state == LifecycleStopped)
            {
                return;
            }

            if (state is LifecycleStarting or LifecycleStopping)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (Interlocked.CompareExchange(ref _lifecycleState, LifecycleStopping, state) != state)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _lifecycleState, LifecycleStopped);
            }

            return;
        }
    }

    public async Task SuspendCaptureAsync(CancellationToken cancellationToken = default)
    {
        using var guard = new StackGuard(nameof(SuspendCaptureAsync));
        if (!guard.Entered)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            LogReentrancyBlocked(nameof(SuspendCaptureAsync));
            return;
        }

        try
        {
            var shouldSuspend = false;

            lock (_stateGate)
            {
                if (_hasActiveSession && !_isSuspended)
                {
                    _isSuspended = true;
                    shouldSuspend = true;
                }
            }

            if (!shouldSuspend)
            {
                return;
            }

            await StopCoreAsync(clearFrame: false, resetPaused: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            scope.Dispose();
        }
    }

    /// <summary>
    /// Resumes a previously suspended capture session.
    /// </summary>
    public void ResumeCapture()
    {
        using var guard = new StackGuard(nameof(ResumeCapture));
        if (!guard.Entered)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            return;
        }

        int ticket;
        bool shouldResume;
        bool useGpu;

        try
        {
            ticket = Interlocked.Increment(ref _resumeTicket);

            lock (_stateGate)
            {
                shouldResume = _hasActiveSession && _isSuspended;
                useGpu = _isGpuActive;
            }

            if (!shouldResume)
            {
                return;
            }
        }
        finally
        {
            scope.Dispose();
        }

        async void ResumeCore()
        {
            if (!TryEnterBusy(out var resumeScope))
            {
                LogReentrancyBlocked(nameof(ResumeCapture));
                return;
            }

            try
            {
                if (_disposed || _target.IsDisposed || ticket != Volatile.Read(ref _resumeTicket))
                {
                    return;
                }

                bool resumeNow;
                lock (_stateGate)
                {
                    resumeNow = _hasActiveSession && _isSuspended && ticket == _resumeTicket;
                    if (resumeNow)
                    {
                        _isSuspended = false;
                    }
                }

                if (!resumeNow)
                {
                    return;
                }

                await StartCoreAsync(useGpu, cancellationToken: CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "ResumeCapture failed during async void execution.");
            }
            finally
            {
                resumeScope.Dispose();
            }
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action(ResumeCore));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "BeginInvoke for ResumeCore failed during shutdown.");
            }

            return;
        }

        ResumeCore();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            return;
        }

        try
        {
            if (IsEditorPreviewDisabled)
            {
                var wasPaused = Interlocked.Exchange(ref _paused, 1) == 1;
                Volatile.Write(ref _previewPaused, true);
                await StopCoreAsync(clearFrame: true, resetPaused: false, cancellationToken).ConfigureAwait(true);
                ApplyPlaceholderFrame();

                if (!wasPaused)
                {
                    _logger.Information("PreviewPaused");
                }

                return;
            }

            var snapshotCaptured = false;
            if (IsEditorSnapshotActive)
            {
                snapshotCaptured = true;
            }
            else
            {
                snapshotCaptured = await CaptureEditorSnapshotAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!snapshotCaptured)
            {
                _logger.Warning(
                    "EditorSnapshot: não foi possível capturar snapshot, mantendo preview contínuo. Motivo={Reason}",
                    "SnapshotFalhou");
                return;
            }

            var wasPausedAfterSnapshot = Interlocked.Exchange(ref _paused, 1) == 1;
            Volatile.Write(ref _previewPaused, true);
            var detached = await DisposeCaptureRetainingFrameAsync(resetPaused: false, cancellationToken)
                .ConfigureAwait(false);

            if (!wasPausedAfterSnapshot || detached)
            {
                _logger.Information("PreviewPaused");
            }

            if (snapshotCaptured && IsEditorSnapshotActive)
            {
                var backend = _isGpuActive ? "GPU" : "GDI";
                _logger.Information(
                    "EditorSnapshot: ativando modo snapshot para monitor={MonitorId}, backend={Backend}",
                    MonitorId,
                    backend);
                _logger.Information("EditorSnapshot: modo snapshot ativado, preview contínuo pausado");
            }

            Volatile.Write(ref _previewRunning, false);
        }
        finally
        {
            scope.Dispose();
        }
    }

    public async Task ResumeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (!TryEnterBusy(out var scope))
        {
            return;
        }

        try
        {
            var wasEditorPreviewDisabled = IsEditorPreviewDisabled;
            if (Volatile.Read(ref _paused) == 0)
            {
                SetEditorPreviewDisabledMode(false);
                return;
            }

            if (wasEditorPreviewDisabled)
            {
                SetEditorPreviewDisabledMode(false);
            }

            var snapshotWasActive = DisableEditorSnapshot(clearImage: true);

            bool useGpu;
            lock (_stateGate)
            {
                useGpu = _isGpuActive;
            }

            var resumed = await StartCoreAsync(useGpu, CancellationToken.None).ConfigureAwait(false);
            if (resumed)
            {
                Volatile.Write(ref _previewRunning, true);
                Volatile.Write(ref _previewPaused, false);
                if (snapshotWasActive)
                {
                    _logger.Information(
                        "EditorSnapshot: modo snapshot desativado, preview contínuo retomado");
                }

                _logger.Information("PreviewResumed");
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

}
