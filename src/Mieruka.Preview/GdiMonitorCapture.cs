using System;
using System.Threading.Tasks;
using Mieruka.Core.Models;

namespace Mieruka.Preview
{
    /// <summary>
    /// Helper responsável por criar sessões de captura via GDI.
    /// </summary>
    public static class GdiMonitorCapture
    {
        /// <summary>
        /// Creates and starts a GDI capture session asynchronously.
        /// </summary>
        public static async Task<IMonitorCapture> CreateAsync(string monitorId)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Monitor preview is only supported on Windows.");
            }

            var monitor = MonitorLocator.Find(monitorId)
                ?? throw new InvalidOperationException($"Monitor '{monitorId}' não foi encontrado.");

            var capture = new GdiMonitorCaptureProvider();
            try
            {
                if (!capture.IsSupported)
                {
                    throw new PlatformNotSupportedException("Captura GDI não suportada neste sistema.");
                }

                await capture.StartAsync(monitor).ConfigureAwait(false);
                return capture;
            }
            catch
            {
                await SafeDisposeAsync(capture).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Creates and starts a GDI capture session synchronously.
        /// </summary>
        /// <remarks>Prefer CreateAsync when possible to avoid blocking the calling thread.</remarks>
        public static IMonitorCapture Create(string monitorId)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Monitor preview is only supported on Windows.");
            }

            var monitor = MonitorLocator.Find(monitorId)
                ?? throw new InvalidOperationException($"Monitor '{monitorId}' não foi encontrado.");

            var capture = new GdiMonitorCaptureProvider();
            try
            {
                if (!capture.IsSupported)
                {
                    throw new PlatformNotSupportedException("Captura GDI não suportada neste sistema.");
                }

                // Use Task.Run to avoid blocking if StartAsync is truly async
                Task.Run(async () => await capture.StartAsync(monitor).ConfigureAwait(false)).ConfigureAwait(false).GetAwaiter().GetResult();
                return capture;
            }
            catch
            {
                SafeDispose(capture);
                throw;
            }
        }

        private static async Task SafeDisposeAsync(IMonitorCapture capture)
        {
            try
            {
                await capture.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore cleanup exceptions when failing to create the capture.
            }
        }

        private static void SafeDispose(IMonitorCapture capture)
        {
            try
            {
                capture.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore cleanup exceptions when failing to create the capture.
            }
        }
    }
}

