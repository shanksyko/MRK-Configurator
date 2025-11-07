using System;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Preview
{
    /// <summary>
    /// Factory estável chamada pelo App para criar capturas por monitor.
    /// </summary>
    public static class CreateForMonitor
    {
        private static readonly ILogger Logger = Log.ForContext("Component", "CaptureFactory");

        /// <summary>Cria captura GPU (Windows.Graphics.Capture / DXGI) para o monitor informado.</summary>
        public static IMonitorCapture Gpu(string monitorId)
        {
            // Hotfix: força caminho GDI para evitar falhas WGC em ambientes instáveis.
            return Gdi(monitorId);
        }

        /// <summary>Cria captura GDI (fallback) para o monitor informado.</summary>
        public static IMonitorCapture Gdi(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            var log = Logger.ForMonitor(monitorId);
            log.Information("CreateForMonitor: selecting backend {Backend}", "GDI");

            return GdiMonitorCapture.Create(monitorId);
        }

        public static bool IsHostSuitableForWgc(MonitorInfo monitor)
        {
            ArgumentNullException.ThrowIfNull(monitor);
            return false;
        }
    }
}

