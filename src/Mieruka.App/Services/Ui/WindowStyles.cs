using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mieruka.App.Services.Ui;

internal static class WindowStyles
{
    private const int GwlExstyle = -20;
    private const int WsExComposited = 0x02000000;

    public static int GetExtendedStyles(nint handle)
    {
        return Environment.Is64BitProcess
            ? (int)GetWindowLongPtr64(handle, GwlExstyle)
            : GetWindowLong32(handle, GwlExstyle);
    }

    public static void SetExtendedStyles(nint handle, int styles)
    {
        if (Environment.Is64BitProcess)
        {
            _ = SetWindowLongPtr64(handle, GwlExstyle, new IntPtr(styles));
        }
        else
        {
            _ = SetWindowLong32(handle, GwlExstyle, styles);
        }
    }

    public static void SetComposited(nint handle, bool enabled)
    {
        var styles = GetExtendedStyles(handle);
        var hasFlag = (styles & WsExComposited) != 0;

        if (enabled && !hasFlag)
        {
            SetExtendedStyles(handle, styles | WsExComposited);
        }
        else if (!enabled && hasFlag)
        {
            SetExtendedStyles(handle, styles & ~WsExComposited);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);
}

internal static class DoubleBufferingHelper
{
    private static readonly MethodInfo? SetStyleMethod = typeof(Control).GetMethod(
        "SetStyle",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty(
        "DoubleBuffered",
        BindingFlags.Instance | BindingFlags.NonPublic);

    public static void EnableOptimizedDoubleBuffering(Control control)
    {
        if (control is null)
        {
            throw new ArgumentNullException(nameof(control));
        }

        Apply(control);
        foreach (Control child in control.Controls)
        {
            EnableOptimizedDoubleBuffering(child);
        }
    }

    private static void Apply(Control control)
    {
        const ControlStyles styles = ControlStyles.AllPaintingInWmPaint |
                                     ControlStyles.OptimizedDoubleBuffer |
                                     ControlStyles.UserPaint;

        SetStyleMethod?.Invoke(control, new object[] { styles, true });
        DoubleBufferedProperty?.SetValue(control, true, null);
    }
}
