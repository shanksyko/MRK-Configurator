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

            var canonicalId = MonitorIdentifier.Normalize(monitorId);
            var log = Logger.ForMonitor(canonicalId);

            if (!GpuCaptureGuard.CanUseGpu())
            {
                log.Information(
                    "CaptureFactory: GPU disabled by guard for {MonitorId}; signalling fallback. key={MonitorKey}",
                    monitorId,
                    canonicalId);
                throw new GraphicsCaptureUnavailableException("GPU disabled by guard.", isPermanent: true);
            }

            log.Information("CaptureFactory: selecting backend {Backend}", "GPU");

            try
            {
                var capture = await WgcMonitorCapture.CreateAsync(canonicalId, cancellationToken).ConfigureAwait(false);
                log.Information(
                    "CaptureFactory: GPU capture ready for {MonitorId}. key={MonitorKey}",
                    monitorId,
                    canonicalId);
                return capture;
            }
            catch (GraphicsCaptureUnavailableException ex)
            {
                log.Warning(
                    ex,
                    "CaptureFactory: GPU unavailable for {MonitorId}; propagating fallback. key={MonitorKey}",
                    monitorId,
                    canonicalId);
                if (ex.IsPermanent)
                {
                    GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailableException");
                }
                throw;
            }
            catch (Exception ex)
            {
                log.Error(
                    ex,
                    "CaptureFactory: GPU backend failed during creation for {MonitorId}; disabling guard. key={MonitorKey}",
                    monitorId,
                    canonicalId);
                GpuCaptureGuard.DisableGpuPermanently($"{ex.GetType().Name}: {ex.Message}");
                throw new GraphicsCaptureUnavailableException("Falha ao inicializar captura GPU.", isPermanent: true, ex);
            }
        }

        /// <summary>Cria captura GPU (Windows.Graphics.Capture / DXGI) de forma síncrona.</summary>
        public static IMonitorCapture Gpu(string monitorId)
        {
            return GpuAsync(monitorId).GetAwaiter().GetResult();
        }

        /// <summary>Cria captura GDI (fallback) para o monitor informado de forma assíncrona.</summary>
        public static async Task<IMonitorCapture> GdiAsync(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            var canonicalId = MonitorIdentifier.Normalize(monitorId);
            var log = Logger.ForMonitor(canonicalId);
            log.Information("CaptureFactory: selecting backend {Backend}", "GDI");

            return await GdiMonitorCapture.CreateAsync(monitorId).ConfigureAwait(false);
        }

        /// <summary>Cria captura GDI (fallback) para o monitor informado.</summary>
        /// <remarks>Prefer GdiAsync when possible to avoid blocking the calling thread.</remarks>
        public static IMonitorCapture Gdi(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            var canonicalId = MonitorIdentifier.Normalize(monitorId);
            var log = Logger.ForMonitor(canonicalId);
            log.Information("CaptureFactory: selecting backend {Backend}", "GDI");

            return GdiMonitorCapture.Create(monitorId);
        }

        public static bool IsHostSuitableForWgc(MonitorInfo monitor)
        {
            ArgumentNullException.ThrowIfNull(monitor);

            var monitorKey = MonitorIdentifier.Create(monitor);

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

            if (GraphicsCaptureProvider.IsGpuInBackoff(monitorKey))
            {
                return false;
            }

            return true;
        }
    }
}

