using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Mieruka.Core.Models;

namespace Mieruka.Core.Interop;

public enum WindowMoveMode
{
    Absolute,
    MonitorRelative,
}

public static class WindowMover
{
    public static void MoveTo(
        IntPtr hwnd,
        MonitorInfo monitor,
        Rectangle boundsPx,
        bool topMost,
        WindowMoveMode mode,
        bool relativeToMonitor,
        bool restoreIfMinimized)
    {
        MoveTo(hwnd, boundsPx, topMost, restoreIfMinimized);
    }

    public static void MoveTo(IntPtr hwnd, Rectangle boundsPx, bool topMost, bool restoreIfMinimized)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Window moving is only supported on Windows.");
        }

        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(hwnd));
        }

        if (!IsWindow(hwnd))
        {
            throw new ArgumentException("The supplied handle does not reference a valid window.", nameof(hwnd));
        }

        if (restoreIfMinimized && IsIconic(hwnd))
        {
            ShowWindow(hwnd, ShowWindowCommand.Restore);
        }

        var frameAdjustment = GetFrameAdjustment(hwnd);
        var targetX = boundsPx.Left + frameAdjustment.Left;
        var targetY = boundsPx.Top + frameAdjustment.Top;
        var targetWidth = boundsPx.Width + frameAdjustment.Right - frameAdjustment.Left;
        var targetHeight = boundsPx.Height + frameAdjustment.Bottom - frameAdjustment.Top;

        IntPtr insertAfter = topMost ? HWND_TOPMOST : HWND_NOTOPMOST;
        const uint flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER;

        if (!SetWindowPos(hwnd, insertAfter, targetX, targetY, targetWidth, targetHeight, flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to move window.");
        }
    }

    public static void Apply(WindowConfig window)
    {
        ArgumentNullException.ThrowIfNull(window);
        // Placeholder para integração futura com posicionamento automático de janelas.
    }

    private static FrameAdjustment GetFrameAdjustment(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var windowRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to get window rect.");
        }

        var result = DwmGetWindowAttribute(
            hwnd,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            out var frameRect,
            Marshal.SizeOf<RECT>());

        if (result == 0)
        {
            return new FrameAdjustment(
                windowRect.Left - frameRect.Left,
                windowRect.Top - frameRect.Top,
                windowRect.Right - frameRect.Right,
                windowRect.Bottom - frameRect.Bottom);
        }

        return FrameAdjustment.Zero;
    }

    private readonly struct FrameAdjustment
    {
        public static FrameAdjustment Zero => new(0, 0, 0, 0);

        public FrameAdjustment(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }
    }

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private enum ShowWindowCommand
    {
        Hide = 0,
        Normal = 1,
        ShowMinimized = 2,
        ShowMaximized = 3,
        ShowNoActivate = 4,
        Show = 5,
        Minimize = 6,
        ShowMinNoActive = 7,
        ShowNA = 8,
        Restore = 9,
        ShowDefault = 10,
        ForceMinimize = 11
    }
}
