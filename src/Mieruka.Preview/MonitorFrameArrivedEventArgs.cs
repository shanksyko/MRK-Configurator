using System;
using System.Drawing;
using System.Runtime.Versioning;

namespace Mieruka.Preview;

/// <summary>
/// Event arguments that wrap a captured monitor frame.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MonitorFrameArrivedEventArgs : EventArgs, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitorFrameArrivedEventArgs"/> class.
    /// </summary>
    /// <param name="bitmap">Captured frame bitmap.</param>
    /// <param name="timestamp">Timestamp associated with the frame.</param>
    public MonitorFrameArrivedEventArgs(Bitmap bitmap, DateTimeOffset timestamp)
    {
        Frame = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Timestamp = timestamp;
    }

    /// <summary>
    /// Gets the captured bitmap. The caller is responsible for disposing the instance
    /// once it is no longer required.
    /// </summary>
    public Bitmap Frame { get; }

    /// <summary>
    /// Gets the captured bitmap. The caller is responsible for disposing the instance
    /// once it is no longer required.
    /// </summary>
    [Obsolete("Use Frame property instead.")]
    public Bitmap Bitmap => Frame;

    /// <summary>
    /// Gets the timestamp associated with the captured frame.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Frame.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
