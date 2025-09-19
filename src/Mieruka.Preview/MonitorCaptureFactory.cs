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
#if WINDOWS10_0_17763_0_OR_GREATER
        if (GraphicsCaptureProvider.IsGraphicsCaptureAvailable)
        {
            return new GraphicsCaptureProvider();
        }
#endif

        return new GdiMonitorCaptureProvider();
    }
}
