using System;
using System.Collections.Generic;
using Mieruka.Core.Config;
using Serilog;

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
        var providers = GetAll();
        if (providers.Count == 0)
        {
            throw new PlatformNotSupportedException("Nenhum provedor de captura de monitor está disponível.");
        }

        if (providers.Count == 1)
        {
            return providers[0];
        }

        return new ResilientMonitorCapture(providers[0], providers[1]);
    }

    /// <summary>
    /// Retrieves the list of available monitor capture providers ordered by preference.
    /// </summary>
    public static IReadOnlyList<IMonitorCapture> GetAll()
    {
        var providers = new List<IMonitorCapture>();

#if WINDOWS10_0_17763_0_OR_GREATER
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && GpuCaptureGuard.CanUseGpu())
        {
            try
            {
                var graphics = new GraphicsCaptureProvider();
                if (graphics.IsSupported)
                {
                    providers.Add(graphics);
                }
                else
                {
                    try { graphics.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                    catch { /* cleanup best-effort */ }
                }
            }
            catch (Exception ex)
            {
                Log.ForContext(typeof(MonitorCaptureFactory))
                    .Warning(ex, "GraphicsCaptureProvider creation failed; falling back to GDI.");
            }
        }
#endif

        providers.Add(new GdiMonitorCaptureProvider());
        return providers;
    }
}
