using System;

namespace Mieruka.Preview;

/// <summary>
/// Creates monitor capture providers respecting platform capabilities.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class MonitorCaptureFactory
{
    /// <summary>
    /// Creates an <see cref="IMonitorCapture"/> instance selecting the best available implementation
    /// for the current platform.
    /// </summary>
    public static IMonitorCapture Create()
    {
        var fallback = new GdiMonitorCaptureProvider();
#if WINDOWS10_0_17763_0_OR_GREATER
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            try
            {
                var graphics = new GraphicsCaptureProvider();
                if (graphics.IsSupported)
                {
                    return new ResilientMonitorCapture(graphics, fallback);
                }

                graphics.DisposeAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore and fall back to GDI capture.
            }
        }
#endif

        return fallback;
    }
}
