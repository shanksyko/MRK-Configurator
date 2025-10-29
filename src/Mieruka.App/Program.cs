using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Mieruka.App.Forms;
using Mieruka.App.Services.Ui;
using Serilog;
using Serilog.Enrichers;
using Serilog.Events;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ConfigureLogging();

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.AddMessageFilter(new MouseMoveCoalescer(16));
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            Log.Information("Iniciando Mieruka Configurator.");

            var mainForm = new MainForm();
            TabLayoutGuard.Attach(mainForm);
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "UnhandledException: fluxo principal encerrou abruptamente.");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void OnThreadException(object? sender, ThreadExceptionEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Error(e.Exception, "ThreadException");
        }
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "UnhandledException");
        }
        else
        {
            Log.Fatal("UnhandledException: {ExceptionObject}", e.ExceptionObject);
        }
    }

    private static void ConfigureLogging()
    {
        const string outputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] (T{ThreadId}) {SourceContext}: {Message:lj}{NewLine}{Exception}";

        var minimumLevel = LogEventLevel.Information;
#if DEBUG
        minimumLevel = LogEventLevel.Debug;
#endif

        var now = DateTime.Now;
        var (logRootDirectory, logDirectory) = ResolveLogDirectories(now);
        PruneOldLogFiles(logRootDirectory, now.AddDays(-14));

        var logFilePath = Path.Combine(logDirectory, $"{now:yyyy-MM-dd}.log");

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "MierukaConfigurator")
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: logFilePath,
                shared: true,
                outputTemplate: outputTemplate,
                rollingInterval: RollingInterval.Infinite,
                retainedFileCountLimit: null);

#if DEBUG
        configuration = configuration.WriteTo.Console(outputTemplate: outputTemplate);
#endif

        Log.Logger = configuration.CreateLogger();
    }

    private static (string logRootDirectory, string logDirectory) ResolveLogDirectories(DateTime timestamp)
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppContext.BaseDirectory,
            Path.GetTempPath()
        };

        var fallbackRoot = Path.Combine(Path.GetTempPath(), "Mieruka", "Logs");
        var fallbackDirectory = Path.Combine(fallbackRoot, timestamp.ToString("yyyy-MM"));

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var root = Path.Combine(candidate, "Mieruka", "Logs");
            var directory = Path.Combine(root, timestamp.ToString("yyyy-MM"));

            if (TryEnsureDirectory(directory))
            {
                return (root, directory);
            }
        }

        TryEnsureDirectory(fallbackDirectory);
        return (fallbackRoot, fallbackDirectory);
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void PruneOldLogFiles(string logRootDirectory, DateTime threshold)
    {
        if (string.IsNullOrWhiteSpace(logRootDirectory))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(logRootDirectory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(logRootDirectory, "*.log", SearchOption.AllDirectories))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < threshold)
                    {
                        fileInfo.Delete();
                    }
                }
                catch
                {
                    // Ignorar falhas ao remover arquivos individuais.
                }
            }
        }
        catch
        {
            // Ignorar falhas ao enumerar ou limpar registros antigos.
        }
    }
}
