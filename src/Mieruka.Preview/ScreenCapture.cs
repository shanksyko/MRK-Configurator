using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Mieruka.Preview;

/// <summary>
/// Provides helpers for capturing screen regions using GDI BitBlt.
/// </summary>
internal static class ScreenCapture
{
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr hdc,
        int x,
        int y,
        int cx,
        int cy,
        IntPtr hdcSrc,
        int x1,
        int y1,
        int rop);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool StretchBlt(
        IntPtr hdcDest,
        int nXOriginDest,
        int nYOriginDest,
        int nWidthDest,
        int nHeightDest,
        IntPtr hdcSrc,
        int nXOriginSrc,
        int nYOriginSrc,
        int nWidthSrc,
        int nHeightSrc,
        int dwRop);

    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    /// <summary>
    /// Captures the provided rectangle from the virtual desktop into an existing bitmap,
    /// using <see cref="System.Drawing.Graphics.GetHdc"/> to BitBlt directly into the
    /// managed bitmap's pixel buffer (no intermediate GDI handle or pixel copy).
    /// </summary>
    /// <param name="src">Rectangle to capture in virtual desktop coordinates.</param>
    /// <param name="target">Reusable bitmap to receive the captured pixels. Must match the target dimensions.</param>
    /// <returns>True if the capture succeeded; false otherwise.</returns>
    public static bool CaptureRectangleInto(Rectangle src, Bitmap target)
    {
        if (src.Width <= 0 || src.Height <= 0 || target is null)
        {
            return false;
        }

        var targetWidth = target.Width;
        var targetHeight = target.Height;

        using var g = Graphics.FromImage(target);
        var hdcDest = g.GetHdc();
        try
        {
            var desktop = GetDesktopWindow();
            var hdcSrc = GetWindowDC(desktop);
            if (hdcSrc == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var rop = SRCCOPY | CAPTUREBLT;

                if (targetWidth == src.Width && targetHeight == src.Height)
                {
                    return BitBlt(hdcDest, 0, 0, targetWidth, targetHeight, hdcSrc, src.Left, src.Top, rop);
                }

                return StretchBlt(
                    hdcDest, 0, 0, targetWidth, targetHeight,
                    hdcSrc, src.Left, src.Top, src.Width, src.Height, rop);
            }
            finally
            {
                ReleaseDC(desktop, hdcSrc);
            }
        }
        finally
        {
            g.ReleaseHdc(hdcDest);
        }
    }

    /// <summary>
    /// Captures the provided rectangle from the virtual desktop into a bitmap.
    /// </summary>
    /// <param name="src">Rectangle to capture in virtual desktop coordinates.</param>
    /// <returns>A bitmap containing the captured pixels.</returns>
    /// <exception cref="ArgumentException">Thrown when the rectangle has invalid dimensions.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the capture fails.</exception>
    public static Bitmap CaptureRectangle(Rectangle src)
        => CaptureRectangle(src, Size.Empty);

    /// <summary>
    /// Captures the provided rectangle from the virtual desktop into a bitmap.
    /// Uses <see cref="System.Drawing.Graphics.GetHdc"/> to BitBlt directly into a
    /// managed bitmap, eliminating the CreateCompatibleBitmap + Image.FromHbitmap
    /// pixel-copy overhead.
    /// </summary>
    /// <param name="src">Rectangle to capture in virtual desktop coordinates.</param>
    /// <param name="targetSize">
    /// Optional target size. When the width or height is zero or negative, the source
    /// dimensions are used (1:1 capture).
    /// </param>
    /// <returns>A bitmap containing the captured pixels.</returns>
    /// <exception cref="ArgumentException">Thrown when the rectangle has invalid dimensions.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the capture fails.</exception>
    public static Bitmap CaptureRectangle(Rectangle src, Size targetSize)
    {
        if (src.Width <= 0 || src.Height <= 0)
        {
            throw new ArgumentException("The capture rectangle dimensions must be positive.", nameof(src));
        }

        var targetWidth = targetSize.Width > 0 ? targetSize.Width : src.Width;
        var targetHeight = targetSize.Height > 0 ? targetSize.Height : src.Height;

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("The target capture size must be positive.", nameof(targetSize));
        }

        // Allocate a managed GDI+ bitmap and BitBlt directly into its backing pixel
        // buffer via GetHdc/ReleaseHdc. This avoids an intermediate CreateCompatibleBitmap
        // plus the pixel-copy inside Image.FromHbitmap.
        var result = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppPArgb);
        try
        {
            using var g = Graphics.FromImage(result);
            var hdcDest = g.GetHdc();
            try
            {
                var desktop = GetDesktopWindow();
                var hdcSrc = GetWindowDC(desktop);
                if (hdcSrc == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to acquire the desktop device context.");
                }

                try
                {
                    var rop = SRCCOPY | CAPTUREBLT;
                    bool success;

                    if (targetWidth == src.Width && targetHeight == src.Height)
                    {
                        success = BitBlt(hdcDest, 0, 0, targetWidth, targetHeight, hdcSrc, src.Left, src.Top, rop);
                    }
                    else
                    {
                        success = StretchBlt(
                            hdcDest,
                            0,
                            0,
                            targetWidth,
                            targetHeight,
                            hdcSrc,
                            src.Left,
                            src.Top,
                            src.Width,
                            src.Height,
                            rop);
                    }

                    if (!success)
                    {
                        throw new InvalidOperationException("GDI failed to copy the desktop pixels into the preview surface.");
                    }
                }
                finally
                {
                    ReleaseDC(desktop, hdcSrc);
                }
            }
            finally
            {
                g.ReleaseHdc(hdcDest);
            }
        }
        catch
        {
            result.Dispose();
            throw;
        }

        return result;
    }
}
