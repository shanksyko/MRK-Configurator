using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using Serilog;

namespace Mieruka.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .CreateLogger();

        try
        {
            ApplicationConfiguration.Initialize();

            using var displayService = CreateDisplayService();
            var store = CreateStore();
            var config = LoadConfiguration(store);
            var monitors = ResolveMonitors(displayService, config);
            var workspace = new ConfiguratorWorkspace(config, monitors);

            using var mainForm = new MainForm(workspace, store, displayService);
            Application.Run(mainForm);

            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Erro inesperado durante a execução.");
            MessageBox.Show($"Erro inesperado: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static JsonStore<GeneralConfig> CreateStore()
    {
        var configDirectory = Path.Combine(AppContext.BaseDirectory, "config");
        var filePath = Path.Combine(configDirectory, "appsettings.json");
        return new JsonStore<GeneralConfig>(filePath);
    }

    private static GeneralConfig LoadConfiguration(JsonStore<GeneralConfig> store)
    {
        try
        {
            return store.LoadAsync().GetAwaiter().GetResult() ?? new GeneralConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao carregar a configuração. Um arquivo padrão será utilizado.");
            return new GeneralConfig();
        }
    }

    private static IReadOnlyList<MonitorInfo> ResolveMonitors(IDisplayService? displayService, GeneralConfig config)
    {
        if (displayService is not null)
        {
            var monitors = displayService.Monitors();
            if (monitors.Count > 0)
            {
                return monitors;
            }
        }

        return config.Monitors;
    }

    private static IDisplayService? CreateDisplayService()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return new DisplayService();
    }
}
