using System;
using System.IO;
using Mieruka.Core.Security;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Mieruka.Core.Services;

/// <summary>
/// Serilog-backed implementation of <see cref="ITelemetry"/>.
/// </summary>
public sealed class TelemetryService : ITelemetry, IDisposable
{
    private const string LogFileName = "telemetry-.log";

    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    private static readonly string MachineId = Environment.MachineName;

    private readonly Logger _logger;

    /// <summary>
    /// Gets the directory where telemetry log files are stored.
    /// </summary>
    public string LogDirectory { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TelemetryService"/> class.
    /// </summary>
    public TelemetryService()
    {
        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        var logDirectory = Path.Combine(baseDirectory, "Mieruka", "logs");
        Directory.CreateDirectory(logDirectory);
        LogDirectory = logDirectory;

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("SessionId", SessionId)
            .Enrich.WithProperty("MachineId", MachineId)
            .WriteTo.File(
                path: Path.Combine(LogDirectory, LogFileName),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true);

#if DEBUG
        configuration = configuration.WriteTo.Console();
#endif

        _logger = configuration.CreateLogger();
    }

    /// <inheritdoc />
    public void Info(string message, Exception? exception = null)
        => Write(LogEventLevel.Information, message, exception);

    /// <inheritdoc />
    public void Warn(string message, Exception? exception = null)
        => Write(LogEventLevel.Warning, message, exception);

    /// <inheritdoc />
    public void Error(string message, Exception? exception = null)
        => Write(LogEventLevel.Error, message, exception);

    private void Write(LogEventLevel level, string message, Exception? exception)
    {
        var sanitizedMessage = Redaction.Redact(message);

        if (exception is null)
        {
            _logger.Write(level, sanitizedMessage);
            return;
        }

        var sanitizedException = Redaction.Redact(exception.Message);
        _logger.Write(level, "{Message} (detalhes: {ExceptionMessage})", sanitizedMessage, sanitizedException);
    }

    /// <summary>
    /// Releases resources used by the logger.
    /// </summary>
    public void Dispose()
    {
        _logger.Dispose();
    }
}
