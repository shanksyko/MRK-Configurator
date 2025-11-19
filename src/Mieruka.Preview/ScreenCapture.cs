using System;
using System.Drawing;
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
    /// Captures the provided rectangle from the virtual desktop into a bitmap.
    /// </summary>
    /// <param name="src">Rectangle to capture in virtual desktop coordinates.</param>
    /// <returns>A bitmap containing the captured pixels.</returns>
    /// <exception cref="ArgumentException">Thrown when the rectangle has invalid dimensions.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the capture fails.</exception>
    public static Bitmap CaptureRectangle(Rectangle src)
        => CaptureRectangle(src, Size.Empty);

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

        var desktop = GetDesktopWindow();
        var hdcSrc = GetWindowDC(desktop);
        if (hdcSrc == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to acquire the desktop device context.");
        }

        try
        {
            var hdcMem = CreateCompatibleDC(hdcSrc);
            if (hdcMem == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create a compatible device context.");
            }

            try
            {
                var hBmp = CreateCompatibleBitmap(hdcSrc, targetWidth, targetHeight);
                if (hBmp == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create the capture bitmap.");
                }

                var hOld = SelectObject(hdcMem, hBmp);
                try
                {
                    var rop = SRCCOPY | CAPTUREBLT;
                    bool success;

                    if (targetWidth == src.Width && targetHeight == src.Height)
                    {
                        success = BitBlt(hdcMem, 0, 0, targetWidth, targetHeight, hdcSrc, src.Left, src.Top, rop);
                    }
                    else
                    {
                        success = StretchBlt(
                            hdcMem,
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

                    var image = Image.FromHbitmap(hBmp);
                    return (Bitmap)image;
                }
                finally
                {
                    SelectObject(hdcMem, hOld);
                    DeleteObject(hBmp);
                }
            }
            finally
            {
                DeleteDC(hdcMem);
            }
        }
        finally
        {
            ReleaseDC(desktop, hdcSrc);
        }
    }
}
