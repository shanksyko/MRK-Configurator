using System.Threading;
using System.Threading.Tasks;

namespace Mieruka.Preview;

/// <summary>
/// Factory helpers for GPU-based monitor capture.
/// </summary>
public static class Preview
{
    public static Task<IMonitorCapture> CreateForMonitorAsync(string monitorId, CancellationToken cancellationToken = default)
    {
        return CaptureFactory.GpuAsync(monitorId, cancellationToken);
    }
}
