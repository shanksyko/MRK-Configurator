using System;
using System.Runtime.InteropServices;

namespace Mieruka.Core.Interop;

public static class User32
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
