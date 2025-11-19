using System;
using System.Drawing;

namespace Mieruka.Core.Models;

/// <summary>
/// Represents the logical resolution used when rendering monitor previews.
/// </summary>
public sealed record class PreviewResolution
{
    private const int TargetWidth = 1280;
    private const int TargetHeight = 720;

    /// <summary>
    /// Gets the logical width of the preview surface in pixels.
    /// </summary>
    public int LogicalWidth { get; init; }

    /// <summary>
    /// Gets the logical height of the preview surface in pixels.
    /// </summary>
    public int LogicalHeight { get; init; }

    /// <summary>
    /// Gets the horizontal scaling factor applied when mapping monitor coordinates to preview space.
    /// </summary>
    public double ScaleX { get; init; }

    /// <summary>
    /// Gets the vertical scaling factor applied when mapping monitor coordinates to preview space.
    /// </summary>
    public double ScaleY { get; init; }

    /// <summary>
    /// Gets a value indicating whether the logical resolution has a valid size.
    /// </summary>
    public bool HasValidSize => LogicalWidth > 0 && LogicalHeight > 0;

    /// <summary>
    /// Creates a <see cref="PreviewResolution"/> instance for the provided monitor metadata.
    /// </summary>
    public static PreviewResolution FromMonitor(MonitorInfo monitor)
    {
        if (monitor is null)
        {
            throw new ArgumentNullException(nameof(monitor));
        }

        var width = monitor.Width > 0 ? monitor.Width : monitor.Bounds.Width;
        var height = monitor.Height > 0 ? monitor.Height : monitor.Bounds.Height;
        return FromDimensions(width, height);
    }

    /// <summary>
    /// Creates a <see cref="PreviewResolution"/> based on the provided physical resolution.
    /// </summary>
    public static PreviewResolution FromDimensions(int monitorWidth, int monitorHeight)
    {
        if (monitorWidth <= 0 || monitorHeight <= 0)
        {
            return new PreviewResolution
            {
                LogicalWidth = 0,
                LogicalHeight = 0,
                ScaleX = 0,
                ScaleY = 0,
            };
        }

        var scaleCandidateX = TargetWidth / (double)monitorWidth;
        var scaleCandidateY = TargetHeight / (double)monitorHeight;
        var scale = Math.Min(scaleCandidateX, scaleCandidateY);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            scale = 1d;
        }

        var logicalWidth = (int)Math.Round(monitorWidth * scale, MidpointRounding.AwayFromZero);
        var logicalHeight = (int)Math.Round(monitorHeight * scale, MidpointRounding.AwayFromZero);

        logicalWidth = Math.Max(1, logicalWidth);
        logicalHeight = Math.Max(1, logicalHeight);

        return new PreviewResolution
        {
            LogicalWidth = logicalWidth,
            LogicalHeight = logicalHeight,
            ScaleX = logicalWidth / (double)monitorWidth,
            ScaleY = logicalHeight / (double)monitorHeight,
        };
    }

    /// <summary>
    /// Creates a <see cref="Size"/> structure that represents the logical preview bounds.
    /// </summary>
    public Size ToSize() => new(LogicalWidth, LogicalHeight);
}
