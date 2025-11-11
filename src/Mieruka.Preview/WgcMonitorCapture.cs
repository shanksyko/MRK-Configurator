using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Mieruka.Core.Config;
using Mieruka.Core.Diagnostics;
using Mieruka.Core.Models;
using Serilog;
using Serilog.Events;

namespace Mieruka.Preview
{
    /// <summary>
    /// Helper responsável por criar sessões de captura via Windows Graphics Capture.
    /// </summary>
    public static class WgcMonitorCapture
    {
        private static readonly ILogger? _logger = Log.ForContext(typeof(WgcMonitorCapture));
        private static readonly ConcurrentDictionary<string, byte> _gpuBackoffLogged = new();
        private static readonly ConcurrentDictionary<string, byte> _gpuUnavailableMonitors = new();
        private static readonly ConcurrentDictionary<string, byte> _hostBlockLogged = new();
        private static readonly object WarnGate = new();
        private static DateTime _lastWarnUtc;

        public static IMonitorCapture Create(string monitorId)
        {
            using var guard = new StackGuard(nameof(Create));
            if (!guard.Entered)
            {
                throw new GraphicsCaptureUnavailableException(
                    "Stack guard bloqueou criação de captura WGC.",
                    isPermanent: false);
            }

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("Monitor preview is only supported on Windows.");
            }

            if (!GpuCaptureGuard.CanUseGpu())
            {
                _logger?.Information(
                    "GPU guard disabled WGC; falling back to GDI for {MonitorId}.",
                    monitorId);
                throw new GraphicsCaptureUnavailableException("GPU disabled by guard.", isPermanent: true);
            }

            EnsureGpuEnvironmentCompatible(monitorId);

            var monitor = MonitorLocator.Find(monitorId)
                ?? throw new InvalidOperationException($"Monitor '{monitorId}' não foi encontrado.");

            if (_gpuUnavailableMonitors.ContainsKey(monitor.Id))
            {
                LogGpuBackoff(monitor.Id, monitor.DeviceName);
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

            if (GraphicsCaptureProvider.IsGpuInBackoff(monitor.Id))
            {
                LogGpuBackoff(monitor.Id, monitor.DeviceName);
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

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
            using var guard = new StackGuard(nameof(StartCapture));
            if (!guard.Entered)
            {
                throw new GraphicsCaptureUnavailableException(
                    "Stack guard bloqueou inicialização de captura WGC.",
                    isPermanent: false);
            }

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
                    LogGpuBackoff(monitor.Id, monitor.DeviceName);
                    WarnRateLimited(ex, "Windows Graphics Capture indisponível para {Monitor}. Aplicando backoff e caindo para GDI. reason={Reason}", monitor.DeviceName, ex.Message);
                }
                GpuCaptureGuard.DisableGpuPermanently("ArgumentException:E_INVALIDARG");
                throw;
            }
            catch (GraphicsCaptureUnavailableException ex)
            {
                var duration = ex.IsPermanent ? Timeout.InfiniteTimeSpan : (TimeSpan?)null;
                var appliedBackoff = GraphicsCaptureProvider.MarkGpuBackoff(monitor.Id, duration);
                var disabledNow = ex.IsPermanent && GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailableException");

                MarkMonitorUnavailable(monitor.Id);

                if (appliedBackoff || disabledNow)
                {
                    LogGpuBackoff(monitor.Id, monitor.DeviceName);
                    var template = ex.IsPermanent
                        ? "Windows Graphics Capture indisponível para {Monitor}. Aplicando backoff indefinido e caindo para GDI. reason={Reason}"
                        : "Windows Graphics Capture indisponível temporariamente para {Monitor}. Aplicando backoff e caindo para GDI. reason={Reason}";
                    WarnRateLimited(ex, template, monitor.DeviceName, ex.Message);
                }

                throw;
            }
            catch (NotSupportedException ex)
            {
                if (GraphicsCaptureProvider.MarkGpuBackoff(monitor.Id))
                {
                    LogGpuBackoff(monitor.Id, monitor.DeviceName);
                    WarnRateLimited(ex, "Windows Graphics Capture não suportado neste host. Caindo para GDI. monitor={Monitor} reason={Reason}", monitor.DeviceName ?? monitor.Id ?? "?", ex.Message);
                }
                GpuCaptureGuard.DisableGpuPermanently("NotSupportedException");
                throw;
            }
        }

        private static void EnsureGpuEnvironmentCompatible(string monitorId)
        {
            using var guard = new StackGuard(nameof(EnsureGpuEnvironmentCompatible));
            if (!guard.Entered)
            {
                throw new GraphicsCaptureUnavailableException(
                    "Stack guard bloqueou validação de ambiente GPU.",
                    isPermanent: false);
            }

            if (GraphicsCaptureProvider.IsGpuInBackoff(monitorId))
            {
                LogGpuBackoff(monitorId, monitorId);
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

            if (_gpuUnavailableMonitors.ContainsKey(monitorId))
            {
                LogGpuBackoff(monitorId, monitorId);
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

            if (WgcEnvironment.IsRemoteSession())
            {
                _logger?.Information(
                    "Sessão remota detectada; captura GPU será ignorada para {MonitorId}.",
                    monitorId);
                GpuCaptureGuard.DisableGpuPermanently("RemoteSession");
                throw new NotSupportedException("Windows Graphics Capture indisponível em sessões remotas.");
            }

            if (!Environment.UserInteractive)
            {
                _logger?.Information(
                    "Ambiente headless detectado; captura GPU será ignorada para {MonitorId}.",
                    monitorId);
                GpuCaptureGuard.DisableGpuPermanently("HeadlessEnvironment");
                throw new NotSupportedException("Windows Graphics Capture indisponível em ambiente headless.");
            }

            if (!GraphicsCaptureProvider.IsGraphicsCaptureAvailable)
            {
                _logger?.Information(
                    "Windows Graphics Capture não está disponível nesta instalação; usando fallback para {MonitorId}.",
                    monitorId);
                GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailable");
                throw new NotSupportedException("Windows Graphics Capture não está disponível neste host.");
            }
        }

        private static void LogGpuBackoff(string monitorId, string? monitorName)
        {
            if (_logger is null)
            {
                return;
            }

            var key = monitorId ?? string.Empty;
            if (!_gpuBackoffLogged.TryAdd(key, 0))
            {
                return;
            }

            var descriptor = string.IsNullOrWhiteSpace(monitorName) ? key : monitorName;
            _logger.Information(
                "Windows Graphics Capture indisponível para {Monitor}. Mantendo fallback GDI nesta sessão.",
                descriptor);
        }

        private static bool ShouldLogWarning()
        {
            var now = DateTime.UtcNow;
            lock (WarnGate)
            {
                if ((now - _lastWarnUtc).TotalSeconds <= 10)
                {
                    return false;
                }

                _lastWarnUtc = now;
                return true;
            }
        }

        private static void WarnRateLimited(Exception exception, string messageTemplate, params object?[] propertyValues)
        {
            if (_logger is null || !ShouldLogWarning())
            {
                return;
            }

            _logger.Warning(exception, messageTemplate, propertyValues);
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

        private static void MarkMonitorUnavailable(string monitorId)
        {
            if (string.IsNullOrWhiteSpace(monitorId))
            {
                return;
            }

            _gpuUnavailableMonitors.TryAdd(monitorId, 0);
        }

        internal static void ReportHostBlock(MonitorInfo monitor, string messageTemplate, LogEventLevel level)
        {
            ArgumentNullException.ThrowIfNull(monitor);

            var monitorKey = !string.IsNullOrWhiteSpace(monitor.Id) ? monitor.Id : monitor.DeviceName;
            if (!string.IsNullOrWhiteSpace(monitorKey))
            {
                GraphicsCaptureProvider.MarkGpuBackoff(monitorKey, Timeout.InfiniteTimeSpan);
                MarkMonitorUnavailable(monitorKey);
                LogGpuBackoff(monitorKey, monitor.DeviceName);
            }

            if (_logger is null)
            {
                return;
            }

            var descriptor = string.IsNullOrWhiteSpace(monitor.DeviceName) ? monitorKey : monitor.DeviceName;
            descriptor ??= "<unknown>";
            var key = string.IsNullOrWhiteSpace(monitorKey) ? descriptor : monitorKey;
            key ??= "<unknown>";

            if (!_hostBlockLogged.TryAdd(key, 0))
            {
                return;
            }

            switch (level)
            {
                case LogEventLevel.Warning:
                    _logger.Warning(messageTemplate, descriptor);
                    break;
                case LogEventLevel.Error:
                    _logger.Error(messageTemplate, descriptor);
                    break;
                case LogEventLevel.Fatal:
                    _logger.Fatal(messageTemplate, descriptor);
                    break;
                case LogEventLevel.Debug:
                    _logger.Debug(messageTemplate, descriptor);
                    break;
                case LogEventLevel.Verbose:
                    _logger.Verbose(messageTemplate, descriptor);
                    break;
                default:
                    _logger.Information(messageTemplate, descriptor);
                    break;
            }
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

