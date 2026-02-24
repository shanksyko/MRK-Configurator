using System;
using System.IO;
using System.Text;

namespace Mieruka.Core.Infra;

/// <summary>
/// Provides a simple thread-safe file logger with daily rolling files.
/// </summary>
public static class Logger
{
    private const string DirectoryName = "Mieruka";
    private const string LogsFolderName = "logs";

    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory;

    static Logger()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        LogDirectory = Path.Combine(localAppData, DirectoryName, LogsFolderName);

        try
        {
            Directory.CreateDirectory(LogDirectory);
        }
        catch
        {
            // Ignore directory creation failures to avoid impacting application flow.
        }
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public static void Info(string message)
        => Write("INFO", message, null);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public static void Warn(string message)
        => Write("WARN", message, null);

    /// <summary>
    /// Logs an error message with an optional exception.
    /// </summary>
    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var filePath = Path.Combine(LogDirectory, $"{now:yyyy-MM-dd}.log");
            var builder = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(' ')
                .Append('[')
                .Append(level)
                .Append(']')
                .Append(' ')
                .Append(message.Trim());

            if (exception is not null)
            {
                builder
                    .AppendLine()
                    .AppendLine("Exception:")
                    .AppendLine(exception.ToString());
            }

            var entry = builder.AppendLine().ToString();

            lock (SyncRoot)
            {
                using var stream = new FileStream(
                    filePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: false);

                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.Write(entry);
            }
        }
        catch
        {
            // Swallow logging exceptions to avoid secondary failures.
        }
    }
}
