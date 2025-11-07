using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;

namespace Mieruka.Preview;

internal static class DwmHelper
{
    private static readonly ILogger Logger = Log.ForContext(typeof(DwmHelper));

    [SupportedOSPlatform("windows")]
    public static bool IsCompositionEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var hr = DwmIsCompositionEnabled(out var enabled);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return enabled;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Falha ao consultar estado do Desktop Window Manager (DWM).");
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(out bool enabled);
}
