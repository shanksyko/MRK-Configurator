using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Mieruka.App.Config;
using Mieruka.App.Forms;
using Serilog;
using Serilog.Events;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var configuration = BuildConfiguration();
        Log.Logger = CreateLogger(configuration);

        try
        {
            Log.Information("Starting MRK Configurator");

            ApplicationConfiguration.Initialize();

            try
            {
                var store = ConfigurationBootstrapper.CreateStore();
                _ = ConfigurationBootstrapper.LoadAsync(store).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to initialize configuration");

                MessageBox.Show(
                    $"Failed to initialize configuration: {ex.Message}",
                    "MRK Configurator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("config", "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine("config", "appsettings.Development.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("MIERUKA_")
            .Build();
    }

    private static ILogger CreateLogger(IConfiguration configuration)
    {
        var loggerConfiguration = new LoggerConfiguration()
            .Enrich.FromLogContext();

        var serilogSection = configuration.GetSection("Logging:Serilog");
        if (serilogSection.Exists())
        {
            loggerConfiguration.ReadFrom.Configuration(configuration, "Logging:Serilog");
        }
        else
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            loggerConfiguration
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "mieruka-.log"),
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
}
