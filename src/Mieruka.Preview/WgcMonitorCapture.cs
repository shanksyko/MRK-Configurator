using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

        public static async Task<IMonitorCapture> CreateAsync(string monitorId, CancellationToken cancellationToken = default)
        {
            using var guard = new StackGuard(nameof(CreateAsync));
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

            var monitor = MonitorLocator.Find(monitorId)
                ?? throw new InvalidOperationException($"Monitor '{monitorId}' não foi encontrado.");

            var monitorKey = GetMonitorBackoffKey(monitor);
            var monitorFriendlyName = GetMonitorFriendlyName(monitor);

            EnsureGpuEnvironmentCompatible(monitorKey, monitorFriendlyName);

            var capture = new GraphicsCaptureProvider();
            try
            {
                if (!capture.IsSupported)
                {
                    throw new PlatformNotSupportedException("Captura por GPU não suportada neste sistema.");
                }

                await StartCaptureAsync(capture, monitor, monitorKey, monitorFriendlyName, cancellationToken)
                    .ConfigureAwait(false);
                return capture;
            }
            catch
            {
                await SafeDisposeAsync(capture).ConfigureAwait(false);
                throw;
            }
        }

        private static async Task StartCaptureAsync(
            IMonitorCapture capture,
            MonitorInfo monitor,
            string monitorKey,
            string monitorFriendlyName,
            CancellationToken cancellationToken)
        {
            using var guard = new StackGuard(nameof(StartCaptureAsync));
            if (!guard.Entered)
            {
                throw new GraphicsCaptureUnavailableException(
                    "Stack guard bloqueou inicialização de captura WGC.",
                    isPermanent: false);
            }

            try
            {
                await capture.StartAsync(monitor, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                if (GraphicsCaptureProvider.MarkGpuBackoff(monitorKey))
                {
                    LogGpuBackoff(monitorKey, monitorFriendlyName);
                    WarnRateLimited(
                        ex,
                        "Windows Graphics Capture indisponível para {MonitorFriendly} (key={MonitorKey}). Aplicando backoff e caindo para GDI. reason={Reason}",
                        monitorFriendlyName,
                        monitorKey,
                        ex.Message);
                }
                GpuCaptureGuard.DisableGpuPermanently("ArgumentException:E_INVALIDARG");
                throw;
            }
            catch (GraphicsCaptureUnavailableException ex)
            {
                var duration = ex.IsPermanent ? Timeout.InfiniteTimeSpan : (TimeSpan?)null;
                var appliedBackoff = GraphicsCaptureProvider.MarkGpuBackoff(monitorKey, duration);
                var disabledNow = ex.IsPermanent && GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailableException");

                MarkMonitorUnavailable(monitorKey);

                if (appliedBackoff || disabledNow)
                {
                    LogGpuBackoff(monitorKey, monitorFriendlyName);
                    var template = ex.IsPermanent
                        ? "Windows Graphics Capture indisponível para {MonitorFriendly} (key={MonitorKey}). Aplicando backoff indefinido e caindo para GDI. reason={Reason}"
                        : "Windows Graphics Capture indisponível temporariamente para {MonitorFriendly} (key={MonitorKey}). Aplicando backoff e caindo para GDI. reason={Reason}";
                    WarnRateLimited(ex, template, monitorFriendlyName, monitorKey, ex.Message);
                }

                throw;
            }
            catch (NotSupportedException ex)
            {
                if (GraphicsCaptureProvider.MarkGpuBackoff(monitorKey))
                {
                    LogGpuBackoff(monitorKey, monitorFriendlyName);
                    WarnRateLimited(
                        ex,
                        "Windows Graphics Capture não suportado neste host. monitor={MonitorFriendly} key={MonitorKey} reason={Reason}",
                        monitorFriendlyName,
                        monitorKey,
                        ex.Message);
                }
                GpuCaptureGuard.DisableGpuPermanently("NotSupportedException");
                throw;
            }
        }

        private static void EnsureGpuEnvironmentCompatible(string monitorKey, string monitorFriendlyName)
        {
            using var guard = new StackGuard(nameof(EnsureGpuEnvironmentCompatible));
            if (!guard.Entered)
            {
                throw new GraphicsCaptureUnavailableException(
                    "Stack guard bloqueou validação de ambiente GPU.",
                    isPermanent: false);
            }

            if (GraphicsCaptureProvider.IsGpuInBackoff(monitorKey))
            {
                LogGpuBackoff(monitorKey, monitorFriendlyName);
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

            if (_gpuUnavailableMonitors.ContainsKey(monitorKey))
            {
                LogGpuBackoff(monitorKey, monitorFriendlyName);
                throw new NotSupportedException("Windows Graphics Capture indisponível para este monitor nesta sessão.");
            }

            if (WgcEnvironment.IsRemoteSession())
            {
                _logger?.Information(
                    "Sessão remota detectada; captura GPU será ignorada para {MonitorFriendly}. key={MonitorKey}",
                    monitorFriendlyName,
                    monitorKey);
                GpuCaptureGuard.DisableGpuPermanently("RemoteSession");
                throw new NotSupportedException("Windows Graphics Capture indisponível em sessões remotas.");
            }

            if (!Environment.UserInteractive)
            {
                _logger?.Information(
                    "Ambiente headless detectado; captura GPU será ignorada para {MonitorFriendly}. key={MonitorKey}",
                    monitorFriendlyName,
                    monitorKey);
                GpuCaptureGuard.DisableGpuPermanently("HeadlessEnvironment");
                throw new NotSupportedException("Windows Graphics Capture indisponível em ambiente headless.");
            }

            if (!GraphicsCaptureProvider.IsGraphicsCaptureAvailable)
            {
                _logger?.Information(
                    "Windows Graphics Capture não está disponível nesta instalação; usando fallback para {MonitorFriendly}. key={MonitorKey}",
                    monitorFriendlyName,
                    monitorKey);
                GpuCaptureGuard.DisableGpuPermanently("GraphicsCaptureUnavailable");
                throw new NotSupportedException("Windows Graphics Capture não está disponível neste host.");
            }
        }

        private static void LogGpuBackoff(string monitorKey, string monitorFriendlyName)
        {
            if (_logger is null)
            {
                return;
            }

            var key = string.IsNullOrWhiteSpace(monitorKey) ? "<unknown>" : monitorKey;
            if (!_gpuBackoffLogged.TryAdd(key, 0))
            {
                return;
            }

            var descriptor = string.IsNullOrWhiteSpace(monitorFriendlyName) ? key : monitorFriendlyName;
            _logger.Information(
                "Windows Graphics Capture indisponível para {MonitorFriendly}. Mantendo fallback GDI nesta sessão. key={MonitorKey}",
                descriptor,
                key);
        }

        private static string GetMonitorBackoffKey(MonitorInfo monitor)
        {
            var key = MonitorIdentifier.Create(monitor);
            if (!string.IsNullOrWhiteSpace(key))
            {
                return key;
            }

            if (!string.IsNullOrWhiteSpace(monitor.Id))
            {
                return monitor.Id;
            }

            if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
            {
                return monitor.DeviceName;
            }

            if (!string.IsNullOrWhiteSpace(monitor.Name))
            {
                return monitor.Name;
            }

            return "<unknown>";
        }

        private static string GetMonitorFriendlyName(MonitorInfo monitor)
        {
            if (!string.IsNullOrWhiteSpace(monitor.Name))
            {
                return monitor.Name;
            }

            if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
            {
                return monitor.DeviceName;
            }

            if (!string.IsNullOrWhiteSpace(monitor.Id))
            {
                return monitor.Id;
            }

            return "<unknown>";
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

        private static async ValueTask SafeDisposeAsync(IMonitorCapture capture)
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

        private static void MarkMonitorUnavailable(string monitorKey)
        {
            if (string.IsNullOrWhiteSpace(monitorKey))
            {
                return;
            }

            _gpuUnavailableMonitors.TryAdd(monitorKey, 0);
        }

        internal static void ReportHostBlock(MonitorInfo monitor, string messageTemplate, LogEventLevel level)
        {
            ArgumentNullException.ThrowIfNull(monitor);

            var monitorKey = GetMonitorBackoffKey(monitor);
            var monitorFriendlyName = GetMonitorFriendlyName(monitor);

            if (!string.IsNullOrWhiteSpace(monitorKey))
            {
                GraphicsCaptureProvider.MarkGpuBackoff(monitorKey, Timeout.InfiniteTimeSpan);
                MarkMonitorUnavailable(monitorKey);
                LogGpuBackoff(monitorKey, monitorFriendlyName);
            }

            if (_logger is null)
            {
                return;
            }

            var descriptor = string.IsNullOrWhiteSpace(monitorFriendlyName) ? monitorKey : monitorFriendlyName;
            if (string.IsNullOrWhiteSpace(descriptor))
            {
                descriptor = "<unknown>";
            }

            var key = string.IsNullOrWhiteSpace(monitorKey) ? descriptor : monitorKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "<unknown>";
            }

            if (!_hostBlockLogged.TryAdd(key, 0))
            {
                return;
            }

            var contextualLogger = _logger
                .ForContext("MonitorKey", key)
                .ForContext("MonitorFriendly", descriptor);

            var enrichedTemplate = string.Concat(
                string.IsNullOrWhiteSpace(messageTemplate) ? string.Empty : messageTemplate.TrimEnd(),
                " monitor={MonitorFriendly} key={MonitorKey}");

            contextualLogger.Write(level, enrichedTemplate, descriptor, key);
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

