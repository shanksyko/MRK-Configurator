using System;

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

            return WgcMonitorCapture.Create(monitorId);
        }

        /// <summary>Cria captura GDI (fallback) para o monitor informado.</summary>
        public static IMonitorCapture Gdi(string monitorId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(monitorId);

            return GdiMonitorCapture.Create(monitorId);
        }
    }
}

