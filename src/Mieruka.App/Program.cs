using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Mieruka.App.Config;
using Mieruka.App.Forms;
using Serilog;
using Serilog.Events;
using Serilog.Settings.Configuration;

namespace Mieruka.App;

internal static class Program
{
    private const string AppDisplayName = "MRK Configurator";
    private const string LogFilePattern = "app-.log";
    private static readonly object MessageBoxGate = new();
    private static string? _logDirectory;

    [STAThread]
    private static void Main()
    {
        EnsureBootstrapLogger();

        try
        {
            ConfigureGlobalExceptionHandlers();

            ApplicationConfiguration.Initialize();

            var configuration = BuildConfiguration();
            TryConfigureLogger(configuration);

            Log.Information("Starting MRK Configurator");

            var warmOk = TryWarmUpConfiguration();
            AppRuntime.SafeMode = !warmOk;

            if (!warmOk)
            {
                Log.Warning("Safe mode activated. A configuration issue was detected during startup.");
            }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            HandleFatalException("Erro inesperado durante a inicialização do aplicativo.", ex);
        }
        finally
        {
            FlushLogger();
        }
    }

    private static void ConfigureGlobalExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) =>
            HandleFatalException("Falha não tratada em thread da interface.", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                HandleFatalException("Falha não tratada na aplicação.", exception);
            }
            else
            {
                try
                {
                    Log.Fatal("Falha não tratada: {Exception}", args.ExceptionObject);
                }
                catch
                {
                    // Ignore logging failures.
                }

                ShowFatalError(
                    "Falha não tratada na aplicação.",
                    args.ExceptionObject?.ToString() ?? "Erro desconhecido.");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            HandleFatalException("Falha não observada em tarefa em segundo plano.", args.Exception);
            args.SetObserved();
        };
    }

    private static void EnsureBootstrapLogger()
    {
        try
        {
            Log.Logger = CreateBootstrapLogger();
        }
        catch
        {
            // Fall back to a silent logger when bootstrap fails.
            Log.Logger = new LoggerConfiguration().CreateLogger();
        }
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder();

        var configurationDirectory = ConfigurationBootstrapper.ResolveConfigurationDirectory();
        if (Directory.Exists(configurationDirectory))
        {
            var persistedSource = new JsonConfigurationSource
            {
                FileProvider = new PhysicalFileProvider(configurationDirectory),
                Path = "appsettings.json",
                Optional = true,
                ReloadOnChange = false,
            };

            persistedSource.ResolveFileProvider();
            builder.Add(persistedSource);
        }

        var baseDirectory = AppContext.BaseDirectory;

        return builder
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("config", "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("config", "appsettings.sample.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("MIERUKA_")
            .Build();
    }

    private static void TryConfigureLogger(IConfiguration configuration)
    {
        try
        {
            var logger = CreateLogger(configuration);
            Log.Logger = logger;
        }
        catch (Exception ex)
        {
            try
            {
                Log.Error(ex, "Failed to apply logging configuration. Continuing with bootstrap logger.");
            }
            catch
            {
                // Ignore logging failures during bootstrap.
            }
        }
    }

    private static ILogger CreateLogger(IConfiguration configuration)
    {
        var loggerConfiguration = new LoggerConfiguration()
            .Enrich.FromLogContext();

        var serilogSection = configuration.GetSection("Logging:Serilog");
        if (serilogSection.Exists())
        {
            loggerConfiguration.ReadFrom.Configuration(
                configuration,
                new ConfigurationReaderOptions { SectionName = "Logging:Serilog" });
        }
        else
        {
            var logDirectory = EnsureLogDirectory();

            loggerConfiguration
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, LogFilePattern),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    restrictedToMinimumLevel: LogEventLevel.Information);
        }

#if DEBUG
        loggerConfiguration.WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Debug);
#else
        if (Debugger.IsAttached)
        {
            loggerConfiguration.WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Debug);
        }
#endif

        return loggerConfiguration.CreateLogger();
    }

    private static ILogger CreateBootstrapLogger()
    {
        foreach (var directory in EnumerateLogDirectories())
        {
            try
            {
                Directory.CreateDirectory(directory);
                _logDirectory = directory;

                return new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        Path.Combine(directory, LogFilePattern),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        restrictedToMinimumLevel: LogEventLevel.Debug)
                    .CreateLogger();
            }
            catch
            {
                // Try the next candidate when the directory cannot be created.
            }
        }

        _logDirectory ??= Path.Combine(Path.GetTempPath(), "Mieruka", "logs");
        return new LoggerConfiguration().CreateLogger();
    }

    private static IEnumerable<string> EnumerateLogDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Mieruka", "logs");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "logs");
        yield return Path.Combine(Path.GetTempPath(), "Mieruka", "logs");
    }

    private static string EnsureLogDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_logDirectory))
        {
            try
            {
                Directory.CreateDirectory(_logDirectory);
            }
            catch
            {
                // Ignore failures and attempt to recover below.
            }

            return _logDirectory!;
        }

        foreach (var directory in EnumerateLogDirectories())
        {
            try
            {
                Directory.CreateDirectory(directory);
                _logDirectory = directory;
                return directory;
            }
            catch
            {
                // Try the next candidate when directory creation fails.
            }
        }

        _logDirectory = Path.Combine(Path.GetTempPath(), "Mieruka", "logs");
        return _logDirectory;
    }

    private static bool TryWarmUpConfiguration()
    {
        var configurationPath = ConfigurationBootstrapper.ResolveConfigurationPath();
        Log.Information("Config path: {CfgPath}", configurationPath);

        try
        {
            ConfigurationBootstrapper.EnsureConfigurationFile();
            ConfigurationBootstrapper.ValidateConfigurationFile(configurationPath);

            var store = ConfigurationBootstrapper.CreateStore();
            _ = ConfigurationBootstrapper.LoadAsync(store).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha ao inicializar a configuração (detalhes acima).");
            return false;
        }
    }

    private static void HandleFatalException(string message, Exception exception)
    {
        try
        {
            Log.Fatal(exception, message);
        }
        catch
        {
            // Ignore logging failures.
        }

        ShowFatalError(message, exception.Message);
    }

    private static void ShowFatalError(string title, string details)
    {
        var logHint = string.IsNullOrWhiteSpace(_logDirectory)
            ? "Os logs não puderam ser gravados."
            : $"Um registro detalhado foi salvo em:{Environment.NewLine}{_logDirectory}";

        var message = string.Join(
            Environment.NewLine + Environment.NewLine,
            title,
            $"Detalhes: {details}",
            logHint);

        try
        {
            lock (MessageBoxGate)
            {
                MessageBox.Show(
                    message,
                    AppDisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        catch
        {
            // Ignore UI failures when showing the error message.
        }
    }

    private static void FlushLogger()
    {
        try
        {
            Log.CloseAndFlush();
        }
        catch
        {
            // Ignore failures while flushing logs during shutdown.
        }
    }
}
