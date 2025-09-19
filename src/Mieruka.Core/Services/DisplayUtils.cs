using System;
using System.Drawing;

namespace Mieruka.Core.Services;

/// <summary>
/// Utility methods for handling display geometry and DPI conversions.
/// </summary>
public static class DisplayUtils
{
    /// <summary>
    /// Converts a pixel value to device-independent pixels using the provided scaling factor.
    /// </summary>
    /// <param name="pixels">Pixel value to convert.</param>
    /// <param name="scale">Display scaling factor (for example, <c>1.25</c> for 125%).</param>
    /// <returns>Equivalent value in device-independent pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scale"/> is less than or equal to zero.</exception>
    public static double PxToDip(double pixels, double scale)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        return pixels / scale;
    }

    /// <summary>
    /// Converts a device-independent pixel value to pixels using the provided scaling factor.
    /// </summary>
    /// <param name="dips">Device-independent pixel value to convert.</param>
    /// <param name="scale">Display scaling factor (for example, <c>1.25</c> for 125%).</param>
    /// <returns>Equivalent value in pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="scale"/> is less than or equal to zero.</exception>
    public static double DipToPx(double dips, double scale)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        return dips * scale;
    }

    /// <summary>
    /// Ensures the specified window bounds are fully contained within the provided work area.
    /// </summary>
    /// <param name="windowBounds">Current window bounds, in pixels.</param>
    /// <param name="workArea">Working area for the monitor, in pixels.</param>
    /// <returns>Adjusted window bounds clamped to the work area.</returns>
    public static Rectangle ClampToWorkArea(Rectangle windowBounds, Rectangle workArea)
    {
        var areaWidth = Math.Max(0, workArea.Width);
        var areaHeight = Math.Max(0, workArea.Height);
        var windowWidth = Math.Max(0, windowBounds.Width);
        var windowHeight = Math.Max(0, windowBounds.Height);

        var width = Math.Min(windowWidth, areaWidth);
        var height = Math.Min(windowHeight, areaHeight);

        var areaLeft = workArea.X;
        var areaTop = workArea.Y;
        var maxX = areaLeft + areaWidth - width;
        var maxY = areaTop + areaHeight - height;

        var x = Math.Clamp(windowBounds.X, areaLeft, maxX);
        var y = Math.Clamp(windowBounds.Y, areaTop, maxY);

        return new Rectangle(x, y, width, height);
    }
}
