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
    private IEnumerable<(string Mode, Func<IMonitorCapture> Factory)> EnumerateFactories(bool preferGpu)
    {
        var monitorKey = MonitorIdentifier.Normalize(MonitorId);
        var gpuInBackoff = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && GraphicsCaptureProvider.IsGpuInBackoff(monitorKey);
        var hostSupportsGpu = false;
        string? monitorFriendlyName = null;

        if (!gpuInBackoff)
        {
            MonitorInfo? monitor = null;
            try
            {
                monitor = MonitorLocator.Find(monitorKey);
            }
            catch
            {
                monitor = null;
            }

            if (monitor is not null)
            {
                monitorFriendlyName = GetMonitorFriendlyName(monitor);
                hostSupportsGpu = CaptureFactory.IsHostSuitableForWgc(monitor);
                if (!hostSupportsGpu)
                {
                    gpuInBackoff = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && GraphicsCaptureProvider.IsGpuInBackoff(monitorKey);
                }
            }
        }

        if (gpuInBackoff)
        {
            monitorFriendlyName ??= MonitorId;
            LogGpuFallback(monitorKey, monitorFriendlyName);
        }

        if (PreviewSafeModeEnabled)
        {
            yield return ("GDI", () => CaptureFactory.Gdi(MonitorId));
            yield break;
        }

        if (preferGpu && !gpuInBackoff && hostSupportsGpu)
        {
            yield return ("GPU", () => CaptureFactory.Gpu(MonitorId));
            yield return ("GDI", () => CaptureFactory.Gdi(MonitorId));
            yield break;
        }

        yield return ("GDI", () => CaptureFactory.Gdi(MonitorId));

        if (!gpuInBackoff && hostSupportsGpu)
        {
            yield return ("GPU", () => CaptureFactory.Gpu(MonitorId));
        }
    }

    private async Task<bool> StartCoreAsync(bool preferGpu, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StartCoreAsync));
        if (!guard.Entered)
        {
            return false;
        }

        if (_disposed)
        {
            return false;
        }

        if (!PreviewRequestedByUser)
        {
            _logger.Debug("PreviewStartSkippedUserRequest");
            return false;
        }

        if (PreviewSafeModeEnabled)
        {
            preferGpu = false;

            if (ShouldDelayStartForSafeMode(out var remainingDelay))
            {
                ScheduleSafeModeStartRetry(remainingDelay);
                return false;
            }

            Interlocked.Exchange(ref _safeModeResumeTicks, 0);
        }
        else
        {
            Interlocked.Exchange(ref _safeModeResumeTicks, 0);
        }

        if (ReadState() == PreviewState.Disposing)
        {
            return false;
        }

        if (!HasUsableTargetArea())
        {
            Interlocked.Exchange(ref _paused, 1);

            lock (_stateGate)
            {
                _hasActiveSession = false;
                _isGpuActive = false;
                _isSuspended = false;
            }

            await StopCoreAsync(clearFrame: false, resetPaused: false, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _lifecycleState, LifecycleStopped);
            return false;
        }

        PreviewState previousState = PreviewState.Stopped;
        var transitionRequested = false;

        while (true)
        {
            var current = ReadState();
            if (current is PreviewState.Running or PreviewState.Starting)
            {
                Volatile.Write(ref _lifecycleState, LifecycleRunning);
                return true;
            }

            if (current == PreviewState.Disposing)
            {
                return false;
            }

            if (current == PreviewState.Pausing)
            {
                PostStart(preferGpu);
                return false;
            }

            if (current is PreviewState.Stopped or PreviewState.Paused)
            {
                if (TryTransition(current, PreviewState.Starting))
                {
                    previousState = current;
                    transitionRequested = true;
                    break;
                }

                continue;
            }

            break;
        }

        if (!transitionRequested)
        {
            return false;
        }

        if (!TryEnterStartStop(nameof(StartAsync), () => PostStart(preferGpu), out var scope))
        {
            SetState(previousState);

            if (_suppressEvents)
            {
                lock (_gate)
                {
                    var running = Capture is not null;
                    Volatile.Write(ref _lifecycleState, running ? LifecycleRunning : LifecycleStopped);
                    return running;
                }
            }

            Volatile.Write(ref _lifecycleState, LifecycleRunning);
            return true;
        }

        using (scope)
        {
            var started = await StartCoreUnsafeAsync(preferGpu, cancellationToken).ConfigureAwait(false);
            if (started)
            {
                SetState(PreviewState.Running);
                Volatile.Write(ref _lifecycleState, LifecycleRunning);
                Volatile.Write(ref _previewRunning, true);
                Volatile.Write(ref _previewPaused, false);
            }
            else
            {
                SetState(previousState);
                Volatile.Write(ref _lifecycleState, LifecycleStopped);
                Volatile.Write(ref _previewRunning, false);
            }

            return started;
        }
    }

    public async Task<bool> StartSafeAsync(bool preferGpu, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = Volatile.Read(ref _lifecycleState);

            if (state == LifecycleRunning)
            {
                return true;
            }

            if (state is LifecycleStarting or LifecycleStopping)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (Interlocked.CompareExchange(ref _lifecycleState, LifecycleStarting, state) != state)
            {
                await WaitForLifecycleAvailabilityAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var started = false;

            try
            {
                started = await StartAsync(preferGpu, cancellationToken).ConfigureAwait(false);
                return started;
            }
            finally
            {
                Volatile.Write(ref _lifecycleState, started ? LifecycleRunning : LifecycleStopped);
            }
        }
    }

    private static Task WaitForLifecycleAvailabilityAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(LifecycleWaitDelay, cancellationToken);
    }

    private bool ShouldDelayStartForSafeMode(out TimeSpan remainingDelay)
    {
        remainingDelay = TimeSpan.Zero;

        if (!PreviewSafeModeEnabled)
        {
            Interlocked.Exchange(ref _safeModeResumeTicks, 0);
            return false;
        }

        var now = DateTime.UtcNow;
        var resumeTicks = Volatile.Read(ref _safeModeResumeTicks);

        if (resumeTicks == 0)
        {
            var resumeAt = now.Add(SafeModeStartDelay).Ticks;
            var previous = Interlocked.CompareExchange(ref _safeModeResumeTicks, resumeAt, 0);
            resumeTicks = previous == 0 ? resumeAt : previous;
        }

        var resumeAtUtc = new DateTime(resumeTicks, DateTimeKind.Utc);

        if (now < resumeAtUtc)
        {
            remainingDelay = resumeAtUtc - now;
            return true;
        }

        Interlocked.Exchange(ref _safeModeResumeTicks, 0);
        return false;
    }

    private void ScheduleSafeModeStartRetry(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        if (Interlocked.CompareExchange(ref _safeModeDelayScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _safeModeDelayScheduled, 0);
            }

            if (_disposed || _suppressEvents)
            {
                return;
            }

            if (_target.IsDisposed)
            {
                return;
            }

            try
            {
                _target.BeginInvoke(new Action(async () =>
                {
                    if (_suppressEvents || _disposed || _target.IsDisposed)
                    {
                        return;
                    }

                    var state = ReadState();
                    if (state is PreviewState.Disposing or PreviewState.Running or PreviewState.Starting)
                    {
                        return;
                    }

                    try
                    {
                        await StartSafeAsync(preferGpu: false).ConfigureAwait(true);
                    }
                    catch (Exception ex)
                    {
                        ForEvent("SafeModeStartRetryFailed")
                            .Error(ex, "Falha ao reiniciar a pré-visualização em modo seguro.");
                        throw;
                    }
                }));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
    }

    private async Task<bool> StartCoreUnsafeAsync(bool preferGpu, CancellationToken cancellationToken)
    {
        using var guard = new StackGuard(nameof(StartCoreUnsafeAsync));
        if (!guard.Entered)
        {
            return false;
        }

        if (_disposed)
        {
            return false;
        }

        if (PreviewSafeModeEnabled)
        {
            preferGpu = false;
            try
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            catch (ThreadInterruptedException)
            {
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        lock (_gate)
        {
            if (Capture is not null)
            {
                return true;
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (UseExternalPreviewHost && !PreviewSafeModeEnabled)
        {
            var externalStarted = await TryStartExternalAsync(preferGpu, cancellationToken).ConfigureAwait(false);
            if (externalStarted)
            {
                Interlocked.Exchange(ref _paused, 0);
                return true;
            }
        }

        if (!PopulateMetadataFromMonitor())
        {
            return false;
        }

        if (_monitorBounds.Width < MinimumCaptureSurface || _monitorBounds.Height < MinimumCaptureSurface)
        {
            _logger.Warning(
                "MonitorPreviewHost: monitor sem superfície válida para captura width={Width} height={Height} monitorId={MonitorId}",
                _monitorBounds.Width,
                _monitorBounds.Height,
                MonitorId);
            return false;
        }

        foreach (var (mode, factory) in EnumerateFactories(preferGpu))
        {
            var isGpu = string.Equals(mode, "GPU", StringComparison.OrdinalIgnoreCase);
            IMonitorCapture? capture = null;
            try
            {
                capture = await CreateCaptureAsync(factory, cancellationToken).ConfigureAwait(false);
                capture.FrameArrived += OnFrameArrived;

                UpdateFrameThrottleForCapture(capture);

                lock (_gate)
                {
                    Capture = capture;
                    Interlocked.Exchange(ref _paused, 0);
                }

                lock (_stateGate)
                {
                    _hasActiveSession = true;
                    _isGpuActive = isGpu;
                    _isSuspended = false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                if (capture is not null)
                {
                    capture.FrameArrived -= OnFrameArrived;
                    await SafeDisposeAsync(capture, cancellationToken).ConfigureAwait(false);
                }

                return false;
            }
            catch (Exception ex)
            {
                var reason = $"{ex.GetType().Name}: {ex.Message}";
                if (preferGpu && isGpu)
                {
                    ForEvent("MonitorFallback")
                        .Warning(
                            ex,
                            "Fallback para GDI habilitado. Monitor={MonitorId}; Fábrica={Factory}; Motivo={Reason}",
                            MonitorId,
                            mode,
                            reason);
                }
                else
                {
                    if (!_suppressEvents)
                    {
                        _logger.Warning(
                            ex,
                            "Falha ao iniciar captura ({Mode}) para {MonitorId}. Motivo={Reason}",
                            mode,
                            MonitorId,
                            reason);
                    }
                }

                if (isGpu)
                {
                    EvaluateGpuDisable(ex);
                }

                if (capture is not null)
                {
                    capture.FrameArrived -= OnFrameArrived;
                    await SafeDisposeAsync(capture, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        lock (_stateGate)
        {
            if (!_isSuspended)
            {
                _hasActiveSession = false;
                _isGpuActive = false;
            }
        }

        return false;
    }

    private static void EvaluateGpuDisable(Exception exception)
    {
        switch (exception)
        {
            case GraphicsCaptureUnavailableException unavailable when unavailable.IsPermanent:
                GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailableException");
                break;
            case ArgumentException:
            case NotSupportedException:
            case InvalidOperationException:
            case System.ComponentModel.Win32Exception:
            case System.Runtime.InteropServices.COMException:
                GpuCaptureGuard.DisableGpuPermanently($"{exception.GetType().Name}: {exception.Message}");
                break;
        }
    }
}
