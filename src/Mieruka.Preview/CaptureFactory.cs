using System;
using Mieruka.Core.Models;

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
            // Hotfix: força caminho GDI para evitar falhas WGC em ambientes instáveis.
            return Gdi(monitorId);
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
            return false;
        }
    }
}

