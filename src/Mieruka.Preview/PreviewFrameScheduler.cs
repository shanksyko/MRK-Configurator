using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Preview;

public sealed class PreviewFrameScheduler
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();

    private CancellationTokenSource? _cancellation;
    private Task? _renderTask;
    private double _currentFps;
    private double _lastFrameDurationMs;
    private long _framesThisWindow;
    private long _totalFrames;
    private TimeSpan _windowStart;

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

    public Task Start(Func<CancellationToken, Task> renderLoopAsync)
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

    private async Task RunRenderLoopAsync(Func<CancellationToken, Task> renderLoopAsync, CancellationToken cancellationToken)
    {
        var targetFrameDuration = TimeSpan.FromSeconds(1 / TargetFps);

        while (!cancellationToken.IsCancellationRequested)
        {
            var frameStart = _stopwatch.Elapsed;

            await renderLoopAsync(cancellationToken).ConfigureAwait(false);

            var frameEnd = _stopwatch.Elapsed;
            var frameDuration = frameEnd - frameStart;

            lock (_gate)
            {
                _lastFrameDurationMs = frameDuration.TotalMilliseconds;
                _totalFrames++;
                _framesThisWindow++;

                var windowDuration = frameEnd - _windowStart;
                if (windowDuration >= TimeSpan.FromSeconds(1))
                {
                    _currentFps = _framesThisWindow / windowDuration.TotalSeconds;
                    _framesThisWindow = 0;
                    _windowStart = frameEnd;
                }
            }

            var delay = targetFrameDuration - frameDuration;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
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
