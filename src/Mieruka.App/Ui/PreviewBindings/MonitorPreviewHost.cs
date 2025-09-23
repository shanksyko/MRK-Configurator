using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Preview;

namespace Mieruka.App.Ui.PreviewBindings;

/// <summary>
/// Describes a monitor that can be used to create preview hosts.
/// </summary>
public interface IMonitorDescriptor
{
    /// <summary>
    /// Gets the unique monitor identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the friendly monitor description.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the monitor metadata.
    /// </summary>
    MonitorInfo Monitor { get; }
}

/// <summary>
/// Manages the lifecycle of a monitor preview capture and binds frames to a <see cref="PictureBox"/>.
/// </summary>
public sealed class MonitorPreviewHost : IDisposable
{
    private readonly IMonitorDescriptor _descriptor;
    private readonly PictureBox _target;
    private readonly bool _preferGpu;
    private readonly object _gate = new();
    private CancellationTokenSource? _startCancellation;
    private Task? _startTask;
    private IMonitorCapture? _capture;
    private Bitmap? _currentFrame;
    private bool _disposed;
    private bool _stopRequested;
    private bool _starting;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorPreviewHost"/> class.
    /// </summary>
    /// <param name="monitor">Monitor descriptor.</param>
    /// <param name="target">PictureBox that receives the captured frames.</param>
    /// <param name="preferGpu">Indicates whether GPU capture should be attempted first.</param>
    public MonitorPreviewHost(IMonitorDescriptor monitor, PictureBox target, bool preferGpu)
    {
        _descriptor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _preferGpu = preferGpu;
    }

    /// <summary>
    /// Gets the monitor identifier.
    /// </summary>
    public string MonitorId => _descriptor.Id;

    /// <summary>
    /// Gets a value indicating whether the preview is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Starts the preview capture.
    /// </summary>
    public void Start()
    {
        if (_disposed || !OperatingSystem.IsWindows())
        {
            return;
        }

        lock (_gate)
        {
            if (_starting || IsRunning)
            {
                return;
            }

            _stopRequested = false;
            _starting = true;
            _startCancellation = new CancellationTokenSource();
            _startTask = Task.Run(() => StartCaptureAsync(_startCancellation.Token));
            _startTask.ContinueWith(static t =>
            {
                var _ = t.Exception;
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    /// <summary>
    /// Stops the preview capture.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task? startTask;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _stopRequested = true;
            cancellation = _startCancellation;
            startTask = _startTask;
            _startCancellation = null;
            _startTask = null;
            _starting = false;
        }

        try
        {
            cancellation?.Cancel();
        }
        catch
        {
            // Ignore cancellation errors.
        }

        cancellation?.Dispose();

        try
        {
            startTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore failures stopping the background start task.
        }

        StopCaptureInternal();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        DisposeFrame();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task StartCaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StopCaptureInternalAsync().ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested || _stopRequested)
            {
                return;
            }

            var providers = CreateProviders();
            for (var index = 0; index < providers.Count; index++)
            {
                var provider = providers[index];
                if (cancellationToken.IsCancellationRequested || _stopRequested)
                {
                    await DisposeRemainingAsync(providers, index).ConfigureAwait(false);
                    return;
                }

                if (!provider.IsSupported)
                {
                    await provider.DisposeAsync().ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await provider.StartAsync(_descriptor.Monitor, cancellationToken).ConfigureAwait(false);
                    provider.FrameArrived += OnFrameArrived;

                    lock (_gate)
                    {
                        _capture = provider;
                        IsRunning = true;
                    }

                    await DisposeRemainingAsync(providers, index + 1).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    provider.FrameArrived -= OnFrameArrived;
                    await provider.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                catch
                {
                    provider.FrameArrived -= OnFrameArrived;
                    await provider.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Ignore start errors; the host will remain stopped.
        }
        finally
        {
            lock (_gate)
            {
                _startCancellation?.Dispose();
                _startCancellation = null;
                _startTask = null;
                _starting = false;
            }
        }
    }

    private IReadOnlyList<IMonitorCapture> CreateProviders()
    {
        var providers = MonitorCaptureFactory.GetAll().ToList();
        if (!_preferGpu && providers.Count > 1)
        {
            providers.Sort(static (left, right) => left is GdiMonitorCaptureProvider ? -1 : right is GdiMonitorCaptureProvider ? 1 : 0);
        }

        return providers;
    }

    private static async Task DisposeRemainingAsync(IReadOnlyList<IMonitorCapture> providers, int startIndex)
    {
        for (var i = startIndex; i < providers.Count; i++)
        {
            try
            {
                await providers[i].DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private void StopCaptureInternal()
    {
        IMonitorCapture? capture;

        lock (_gate)
        {
            capture = _capture;
            _capture = null;
            IsRunning = false;
        }

        if (capture is null)
        {
            return;
        }

        capture.FrameArrived -= OnFrameArrived;

        try
        {
            capture.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore stop failures.
        }

        try
        {
            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore dispose failures.
        }
    }

    private async Task StopCaptureInternalAsync()
    {
        IMonitorCapture? capture;

        lock (_gate)
        {
            capture = _capture;
            _capture = null;
            IsRunning = false;
        }

        if (capture is null)
        {
            return;
        }

        capture.FrameArrived -= OnFrameArrived;

        try
        {
            await capture.StopAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore stop failures.
        }

        try
        {
            await capture.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Ignore dispose failures.
        }
    }

    private void OnFrameArrived(object? sender, MonitorFrameArrivedEventArgs e)
    {
        Bitmap? frame = null;
        try
        {
            var bitmap = e.Bitmap;
            frame = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), bitmap.PixelFormat);
        }
        catch
        {
            // Ignore frame cloning issues.
        }
        finally
        {
            e.Dispose();
        }

        if (frame is null)
        {
            return;
        }

        UpdateTarget(frame);
    }

    private void UpdateTarget(Bitmap frame)
    {
        if (_target.IsDisposed)
        {
            frame.Dispose();
            return;
        }

        if (_target.InvokeRequired)
        {
            try
            {
                _target.BeginInvoke(new Action<Bitmap>(UpdateTarget), frame);
            }
            catch (ObjectDisposedException)
            {
                frame.Dispose();
            }
            catch (InvalidOperationException)
            {
                frame.Dispose();
            }

            return;
        }

        var previous = _currentFrame;
        _currentFrame = frame;
        _target.Image = frame;
        previous?.Dispose();
    }

    private void DisposeFrame()
    {
        if (_target.InvokeRequired)
        {
            try
            {
                _target.Invoke(new Action(DisposeFrame));
                return;
            }
            catch
            {
                // Ignore invoke errors during shutdown.
            }
        }

        if (_target.Image == _currentFrame)
        {
            _target.Image = null;
        }

        _currentFrame?.Dispose();
        _currentFrame = null;
    }
}
