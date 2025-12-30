using System;
using System.Threading.Tasks;

namespace Mieruka.Preview;

/// <summary>
/// Factory helpers for GDI-based monitor capture.
/// </summary>
public static class GdiCapture
{
    /// <summary>
    /// Creates a GDI capture for the specified monitor asynchronously.
    /// </summary>
    public static async Task<IMonitorCapture> CreateForMonitorAsync(string monitorId)
    {
        return await GdiMonitorCapture.CreateAsync(monitorId).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a GDI capture for the specified monitor synchronously.
    /// </summary>
    /// <remarks>Prefer CreateForMonitorAsync when possible to avoid blocking the calling thread.</remarks>
    public static IMonitorCapture CreateForMonitor(string monitorId)
    {
        return GdiMonitorCapture.Create(monitorId);
    }
}
