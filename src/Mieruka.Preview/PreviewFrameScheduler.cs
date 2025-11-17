using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private long _nextFrameAtTicks;
    private long _completedFrames;
    private long _totalProcessingTicks;

    // TODO: Document where PreviewFrameScheduler instances are created and how they are injected into preview components.
    public PreviewFrameScheduler(double targetFramesPerSecond)
    {
        if (double.IsNaN(targetFramesPerSecond) || double.IsInfinity(targetFramesPerSecond) || targetFramesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFramesPerSecond));
        }

        TargetFps = targetFramesPerSecond;
        _stopwatch.Start();
    }

    public double TargetFps { get; }

    public void Start(Func<CancellationToken, Task> frameProducer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frameProducer);

        lock (_gate)
        {
            if (_cts is not null)
            {
                throw new InvalidOperationException("Frame scheduler already started.");
            }

            _nextFrameAtTicks = 0;
            _completedFrames = 0;
            _totalProcessingTicks = 0;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _worker = Task.Run(() => RunAsync(frameProducer, _cts.Token), CancellationToken.None);
        }
    }

    public bool TryBeginFrame(out long timestampTicks)
    {
        if (renderLoopAsync is null)
        {
            throw new ArgumentNullException(nameof(renderLoopAsync));
        }

        lock (_gate)
        {
            if (_renderTask != null && !_renderTask.IsCompleted)
            {
                return Task.CompletedTask;
            }

            _windowStart = _stopwatch.Elapsed;
            _cancellation = new CancellationTokenSource();
            _renderTask = Task.Run(() => RunRenderLoopAsync(renderLoopAsync, _cancellation.Token), CancellationToken.None);
            return Task.CompletedTask;
        }
    }

    public async Task Stop()
    {
        Task? renderTask;
        lock (_gate)
        {
            if (_cancellation == null)
            {
                return;
            }

            _cancellation.Cancel();
            renderTask = _renderTask;
            _cancellation = null;
            _renderTask = null;
        }

        if (renderTask is not null)
        {
            try
            {
                await renderTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping the scheduler.
            }
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            cts = _cts;
            worker = _worker;
            _cts = null;
            _worker = null;
        }

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested.
        }
        finally
        {
            cts.Dispose();
        }
    }

    public PreviewFrameSchedulerMetrics GetMetricsAndReset()
    {
        lock (_gate)
        {
            var metrics = new PreviewFrameSchedulerMetrics(
                _currentFps,
                _lastFrameDurationMs,
                _framesThisWindow,
                _totalFrames);

            _currentFps = 0;
            _lastFrameDurationMs = 0;
            _framesThisWindow = 0;
            _windowStart = _stopwatch.Elapsed;

            return metrics;
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> frameProducer, CancellationToken cancellationToken)
    {
        await Task.Yield();

        while (!cancellationToken.IsCancellationRequested)
        {
            long frameStartTimestamp;
            if (!TryBeginFrame(out frameStartTimestamp))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await frameProducer(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Swallow unexpected exceptions to keep the loop alive.
            }
            finally
            {
                EndFrame(frameStartTimestamp);
            }
        }
    }
}

public readonly struct PreviewFrameSchedulerMetrics
{
    public PreviewFrameSchedulerMetrics(double currentFps, double lastFrameDurationMs, long framesThisWindow, long totalFrames)
    {
        CurrentFps = currentFps;
        LastFrameDurationMs = lastFrameDurationMs;
        FramesThisWindow = framesThisWindow;
        TotalFrames = totalFrames;
    }

    public double CurrentFps { get; }

    public double LastFrameDurationMs { get; }

    public long FramesThisWindow { get; }

    public long TotalFrames { get; }
}
