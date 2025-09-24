namespace Mieruka.Preview;

/// <summary>
/// Factory helpers for GDI-based monitor capture.
/// </summary>
public static class GdiCapture
{
    public static IMonitorCapture CreateForMonitor(string monitorId)
    {
        return GdiMonitorCapture.Create(monitorId);
    }
}
