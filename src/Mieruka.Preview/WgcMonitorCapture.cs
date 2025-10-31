using System;
using System.Runtime.InteropServices;
using System.Threading;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.Preview
{
    /// <summary>
    /// Helper responsável por criar sessões de captura via Windows Graphics Capture.
    /// </summary>
    public static class WgcMonitorCapture
    {
        private static readonly ILogger? _logger = Log.ForContext(typeof(WgcMonitorCapture));

        public static IMonitorCapture Create(string monitorId)
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Monitor preview is only supported on Windows.");
            }

            EnsureGpuEnvironmentCompatible(monitorId);

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
            try
            {
                var task = capture.StartAsync(monitor);
                if (!task.IsCompleted)
                {
                    task.GetAwaiter().GetResult();
                }
            }
            catch (ArgumentException ex)
            {
                if (GraphicsCaptureProvider.MarkGpuBackoff(monitor.Id))
                {
                    _logger?.Warn(ex, "Windows Graphics Capture indisponível para {Monitor}. Aplicando backoff e caindo para GDI.", monitor.DeviceName);
                }
                throw;
            }
            catch (GraphicsCaptureUnavailableException ex)
            {
                var duration = ex.IsPermanent ? Timeout.InfiniteTimeSpan : (TimeSpan?)null;
                var appliedBackoff = GraphicsCaptureProvider.MarkGpuBackoff(monitor.Id, duration);
                var disabledNow = ex.IsPermanent && GraphicsCaptureProvider.DisableGpuGlobally();

                if (appliedBackoff || disabledNow)
                {
                    var template = ex.IsPermanent
                        ? "Windows Graphics Capture indisponível para {Monitor}. Aplicando backoff indefinido e caindo para GDI."
                        : "Windows Graphics Capture indisponível temporariamente para {Monitor}. Aplicando backoff e caindo para GDI.";
                    _logger?.Warn(ex, template, monitor.DeviceName);
                }

                throw;
            }
            catch (NotSupportedException ex)
            {
                if (GraphicsCaptureProvider.MarkGpuBackoff(monitor.Id))
                {
                    _logger?.Warn(ex, "Windows Graphics Capture não suportado neste host. Caindo para GDI.");
                }
                throw;
            }
        }

        private static void EnsureGpuEnvironmentCompatible(string monitorId)
        {
            if (WgcEnvironment.IsRemoteSession())
            {
                _logger?.Information(
                    "Sessão remota detectada; captura GPU será ignorada para {MonitorId}.",
                    monitorId);
                throw new NotSupportedException("Windows Graphics Capture indisponível em sessões remotas.");
            }

            if (!Environment.UserInteractive)
            {
                _logger?.Information(
                    "Ambiente headless detectado; captura GPU será ignorada para {MonitorId}.",
                    monitorId);
                throw new NotSupportedException("Windows Graphics Capture indisponível em ambiente headless.");
            }

            if (!GraphicsCaptureProvider.IsGraphicsCaptureAvailable)
            {
                _logger?.Information(
                    "Windows Graphics Capture não está disponível nesta instalação; usando fallback para {MonitorId}.",
                    monitorId);
                throw new NotSupportedException("Windows Graphics Capture não está disponível neste host.");
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

    internal static class LoggerExtensions
    {
        public static void Warn(this ILogger logger, Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            logger.Warning(exception, messageTemplate, propertyValues);
        }
    }

    internal static class WgcEnvironment
    {
        private const int SmRemoteSession = 0x1000;

        public static bool IsRemoteSession()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                return GetSystemMetrics(SmRemoteSession) != 0;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }
}

