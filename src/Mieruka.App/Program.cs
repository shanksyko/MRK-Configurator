using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Mieruka.App.Forms;
using Mieruka.App.Services.Ui;
using Serilog;
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
        const string directoryName = "MierukaConfiguratorPro";
        const string logsFolderName = "logs";
        const string logFileName = "mieruka-.log";
        const string outputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] (T{ThreadId}) {SourceContext}: {Message:lj}{NewLine}{Exception}";

        var minimumLevel = LogEventLevel.Information;
#if DEBUG
        minimumLevel = LogEventLevel.Debug;
#endif

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = AppContext.BaseDirectory;
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Path.GetTempPath();
        }

        var logDirectory = Path.Combine(baseDirectory, directoryName, logsFolderName);

        try
        {
            Directory.CreateDirectory(logDirectory);
        }
        catch
        {
            // Ignorar falhas ao criar diretórios para não impactar a inicialização.
        }

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "MierukaConfigurator")
            .Enrich.WithThreadId()
            .WriteTo.File(
                path: Path.Combine(logDirectory, logFileName),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true,
                outputTemplate: outputTemplate);

#if DEBUG
        configuration = configuration.WriteTo.Console(outputTemplate: outputTemplate);
#endif

        Log.Logger = configuration.CreateLogger();
    }
}
