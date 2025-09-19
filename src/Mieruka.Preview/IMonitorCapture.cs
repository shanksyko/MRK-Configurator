using System;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Models;

namespace Mieruka.Preview;

/// <summary>
/// Abstraction for capturing frames from a monitor source.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public interface IMonitorCapture : IAsyncDisposable
{
    /// <summary>
    /// Occurs when a new monitor frame is available.
    /// </summary>
    event EventHandler<MonitorFrameArrivedEventArgs>? FrameArrived;

    /// <summary>
    /// Gets a value indicating whether the implementation can operate on the current platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Starts capturing the specified monitor.
    /// </summary>
    /// <param name="monitor">Monitor metadata.</param>
    /// <param name="cancellationToken">Token used to cancel the asynchronous operation.</param>
    Task StartAsync(MonitorInfo monitor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the ongoing capture and releases unmanaged resources.
    /// </summary>
    ValueTask StopAsync();
}
