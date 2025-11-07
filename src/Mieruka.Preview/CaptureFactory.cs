using System;
using Mieruka.Core.Models;
using Mieruka.Preview.Capture.Interop;
using Serilog.Events;

namespace Mieruka.Preview
{
    /// <summary>
    /// Factory estável chamada pelo App para criar capturas por monitor.
    /// </summary>
    public static class CreateForMonitor
    {
        /// <summary>Cria captura GPU (Windows.Graphics.Capture / DXGI) para o monitor informado.</summary>
        public static IMonitorCapture Gpu(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Monitor preview is only supported on Windows.");
            }

            var monitor = MonitorLocator.Find(monitorId)
                ?? throw new InvalidOperationException($"Monitor '{monitorId}' não foi encontrado.");

            if (!IsHostSuitableForWgc(monitor))
            {
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

            return WgcMonitorCapture.Create(monitorId);
        }

        /// <summary>Cria captura GDI (fallback) para o monitor informado.</summary>
        public static IMonitorCapture Gdi(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            return GdiMonitorCapture.Create(monitorId);
        }

        public static bool IsHostSuitableForWgc(MonitorInfo monitor)
        {
            ArgumentNullException.ThrowIfNull(monitor);

            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            if (WgcEnvironment.IsRemoteSession())
            {
                WgcMonitorCapture.ReportHostBlock(
                    monitor,
                    "Sessão remota detectada; Windows Graphics Capture indisponível para {Monitor}.",
                    LogEventLevel.Information);
                return false;
            }

            if (!DwmHelper.IsCompositionEnabled())
            {
                WgcMonitorCapture.ReportHostBlock(
                    monitor,
                    "Desktop Window Manager desabilitado; Windows Graphics Capture indisponível para {Monitor}.",
                    LogEventLevel.Warning);
                return false;
            }

            if (!MonitorUtilities.TryGetMonitorHandle(monitor.DeviceName, out var monitorHandle, out _)
                || monitorHandle == IntPtr.Zero)
            {
                WgcMonitorCapture.ReportHostBlock(
                    monitor,
                    "Falha ao resolver HMONITOR; Windows Graphics Capture indisponível para {Monitor}.",
                    LogEventLevel.Warning);
                return false;
            }

            if (!GraphicsCaptureInterop.IsWgcRuntimeSupported())
            {
                WgcMonitorCapture.ReportHostBlock(
                    monitor,
                    "Runtime do Windows Graphics Capture indisponível; utilizando fallback GDI para {Monitor}.",
                    LogEventLevel.Warning);
                return false;
            }

            return true;
        }
    }
}

