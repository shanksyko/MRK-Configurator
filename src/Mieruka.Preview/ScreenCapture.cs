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
    {
        if (src.Width <= 0 || src.Height <= 0)
        {
            throw new ArgumentException("The capture rectangle dimensions must be positive.", nameof(src));
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
                var hBmp = CreateCompatibleBitmap(hdcSrc, src.Width, src.Height);
                if (hBmp == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create the capture bitmap.");
                }

                var hOld = SelectObject(hdcMem, hBmp);
                try
                {
                    if (!BitBlt(hdcMem, 0, 0, src.Width, src.Height, hdcSrc, src.Left, src.Top, SRCCOPY | CAPTUREBLT))
                    {
                        throw new InvalidOperationException("BitBlt failed to copy the desktop pixels.");
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
