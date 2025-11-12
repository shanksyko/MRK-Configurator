using System;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.Core.Config;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Preview
{
    /// <summary>
    /// Factory estável chamada pelo App para criar capturas por monitor.
    /// </summary>
    public static class CaptureFactory
    {
        private static readonly ILogger Logger = Log.ForContext("Component", "CaptureFactory");

        /// <summary>Cria captura GPU (Windows.Graphics.Capture / DXGI) para o monitor informado.</summary>
        public static async Task<IMonitorCapture> GpuAsync(string monitorId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            var log = Logger.ForMonitor(monitorId);

            if (!GpuCaptureGuard.CanUseGpu())
            {
                log.Information("CaptureFactory: GPU disabled by guard for {MonitorId}; signalling fallback.", monitorId);
                throw new GraphicsCaptureUnavailableException("GPU disabled by guard.", isPermanent: true);
            }

            log.Information("CaptureFactory: selecting backend {Backend}", "GPU");

            try
            {
                var capture = await WgcMonitorCapture.CreateAsync(monitorId, cancellationToken).ConfigureAwait(false);
                log.Information("CaptureFactory: GPU capture ready for {MonitorId}", monitorId);
                return capture;
            }
            catch (GraphicsCaptureUnavailableException ex)
            {
                log.Warning(ex, "CaptureFactory: GPU unavailable for {MonitorId}; propagating fallback.", monitorId);
                if (ex.IsPermanent)
                {
                    GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailableException");
                }
                throw;
            }
            catch (Exception ex)
            {
                log.Error(ex, "CaptureFactory: GPU backend failed during creation for {MonitorId}; disabling guard.", monitorId);
                GpuCaptureGuard.DisableGpuPermanently($"{ex.GetType().Name}: {ex.Message}");
                throw new GraphicsCaptureUnavailableException("Falha ao inicializar captura GPU.", isPermanent: true, ex);
            }
        }

        /// <summary>Cria captura GPU (Windows.Graphics.Capture / DXGI) de forma síncrona.</summary>
        public static IMonitorCapture Gpu(string monitorId)
        {
            return GpuAsync(monitorId).GetAwaiter().GetResult();
        }

        /// <summary>Cria captura GDI (fallback) para o monitor informado.</summary>
        public static IMonitorCapture Gdi(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            var log = Logger.ForMonitor(monitorId);
            log.Information("CaptureFactory: selecting backend {Backend}", "GDI");

            return GdiMonitorCapture.Create(monitorId);
        }

        public static bool IsHostSuitableForWgc(MonitorInfo monitor)
        {
            ArgumentNullException.ThrowIfNull(monitor);

            if (!GpuCaptureGuard.CanUseGpu())
            {
                return false;
            }

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                return false;
            }

            if (WgcEnvironment.IsRemoteSession())
            {
                return false;
            }

            if (!Environment.UserInteractive)
            {
                return false;
            }

            if (!DwmHelper.IsCompositionEnabled())
            {
                return false;
            }

            try
            {
                if (!GraphicsCaptureProvider.IsGraphicsCaptureAvailable)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            if (GraphicsCaptureProvider.IsGpuGloballyDisabled)
            {
                return false;
            }

            if (GraphicsCaptureProvider.IsGpuInBackoff(monitor.Id))
            {
                return false;
            }

            return true;
        }
    }
}

