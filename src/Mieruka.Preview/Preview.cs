using System;
using System.Threading.Tasks;
using Mieruka.Core.Models;

namespace Mieruka.Preview;

/// <summary>
/// Factory helpers for GPU-based monitor capture.
/// </summary>
public static class Preview
{
    public static IMonitorCapture CreateForMonitor(string monitorId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Monitor preview is only supported on Windows.");
        }

        var monitor = MonitorLocator.Find(monitorId)
            ?? throw new InvalidOperationException($"Monitor '{monitorId}' não foi encontrado.");

        var capture = new GraphicsCaptureProvider();
        try
        {
            if (!capture.IsSupported)
            {
                throw new PlatformNotSupportedException("Captura por GPU não suportada neste sistema.");
            }

            StartCapture(capture, monitor);
            return capture;
        }
        catch
        {
            SafeDispose(capture);
            throw;
        }
    }

    private static void StartCapture(IMonitorCapture capture, MonitorInfo monitor)
    {
        var task = capture.StartAsync(monitor);
        if (!task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
        }
    }

    private static void SafeDispose(IMonitorCapture capture)
    {
        try
        {
            capture.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore cleanup exceptions when failing to create the capture.
        }
    }
}
