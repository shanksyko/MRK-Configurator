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

                Task.Run(() => capture.StartAsync(monitor)).GetAwaiter().GetResult();
                return capture;
            }
            catch
            {
                try { capture.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { /* cleanup failure is secondary */ }
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
    }
}

