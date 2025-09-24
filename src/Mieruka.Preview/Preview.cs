namespace Mieruka.Preview;

/// <summary>
/// Factory helpers for GPU-based monitor capture.
/// </summary>
public static class Preview
{
    public static IMonitorCapture CreateForMonitor(string monitorId)
    {
        return WgcMonitorCapture.Create(monitorId);
    }
}
