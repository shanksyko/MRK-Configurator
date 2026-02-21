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
    public void Dispose()
    {
        if (!_lifecycleGate.TryEnter())
        {
            ScheduleDeferredDispose();
            return;
        }

        try
        {
            _suppressEvents = true;

            if (!DisposeInternal())
            {
                ScheduleDeferredDispose();
            }
        }
        finally
        {
            _lifecycleGate.Exit();
        }
    }

    private bool DisposeInternal(bool allowRetry = false)
    {
        if (_disposed)
        {
            return true;
        }

        PreviewState previousState;
        if (!TryTransition(new[]
            {
                PreviewState.Running,
                PreviewState.Starting,
                PreviewState.Pausing,
                PreviewState.Paused,
                PreviewState.Stopped,
            }, PreviewState.Disposing, out previousState))
        {
            if (ReadState() != PreviewState.Disposing)
            {
                return false;
            }

            previousState = PreviewState.Disposing;
        }

        if (!TryEnterStartStop(nameof(Dispose), retryAction: null, out var scope))
        {
            if (previousState != PreviewState.Disposing)
            {
                SetState(previousState);
            }

            return false;
        }

        using (scope)
        {
            _disposed = true;

            lock (_stateGate)
            {
                _hasActiveSession = false;
                _isGpuActive = false;
                _isSuspended = false;
            }

            DisableEditorSnapshot(clearImage: true);

            StopCoreUnsafeAsync(clearFrame: true, resetPaused: true, cancellationToken: CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            DisposePlaceholderBitmap();
            DisposeBitmapPool();

            GC.SuppressFinalize(this);
        }

        return true;
    }

    private void ScheduleDeferredDispose()
    {
        if (Interlocked.Exchange(ref _disposeRetryScheduled, 1) != 0)
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                while (!DisposeInternal())
                {
                    await Task.Delay(15).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    ForEvent("DisposeRetryFailed").Error(ex, "Falha ao concluir Dispose após reagendamento.");
                }
                catch
                {
                    // Swallow logging failures during shutdown.
                }
            }
            finally
            {
                Interlocked.Exchange(ref _disposeRetryScheduled, 0);
            }
        });
    }

    private bool TryEnterBusy(out BusyScope scope)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            scope = default;
            return false;
        }

        scope = new BusyScope(this);
        return true;
    }

    private void ExitBusy()
    {
        Interlocked.Exchange(ref _busy, 0);
    }

    private bool TryEnterStartStop(string operation, Action? retryAction, out StartStopScope scope)
    {
        if (Interlocked.CompareExchange(ref _inStartStop, 1, 0) != 0)
        {
            scope = default;
            if (!_suppressEvents)
            {
                LogReentrancyBlocked(operation);
                retryAction?.Invoke();
            }

            return false;
        }

        scope = new StartStopScope(this);
        return true;
    }

    private void PostDispose()
    {
        if (_suppressEvents || _target.IsDisposed)
        {
            return;
        }

        try
        {
            _target.BeginInvoke(new Action(() =>
            {
                try
                {
                    DisposeInternal(allowRetry: false);
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

    private void ExitStartStop()
    {
        Volatile.Write(ref _inStartStop, 0);
    }
}
