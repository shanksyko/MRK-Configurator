using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Services;
using Mieruka.App.Tray;
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

            using var telemetry = new TelemetryService();
            var displayService = CreateDisplayService(telemetry);

            try
            {
                using var bindingService = displayService is not null ? new BindingService(displayService, telemetry) : null;
                using var cycleManager = bindingService is not null ? new CycleManager(bindingService, telemetry: telemetry) : null;
                using var watchdogService = bindingService is not null ? new WatchdogService(bindingService, telemetry) : null;
                using var updaterService = new UpdaterService(telemetry);

                IOrchestrationComponent monitorComponent = bindingService is not null
                    ? new BindingOrchestrationComponent(bindingService, telemetry)
                    : NullOrchestrationComponent.Instance;
                IOrchestrationComponent rotationComponent = NullOrchestrationComponent.Instance;
                var cycleComponent = cycleManager as IOrchestrationComponent ?? NullOrchestrationComponent.Instance;
                var watchdogComponent = watchdogService as IOrchestrationComponent ?? NullOrchestrationComponent.Instance;
                var orchestrator = new Orchestrator(monitorComponent, rotationComponent, cycleComponent, watchdogComponent, telemetry);

                var store = CreateStore();
                var migrator = new ConfigMigrator();
                var config = LoadConfiguration(store, migrator);
                var monitors = ResolveMonitors(displayService, config);
                var workspace = new ConfiguratorWorkspace(config, monitors);

                void ApplyConfiguration(GeneralConfig candidate)
                {
                    cycleManager?.ApplyConfiguration(candidate);
                    watchdogService?.ApplyConfiguration(candidate);
                    updaterService.ApplyConfiguration(candidate.AutoUpdate);
                }

                ApplyConfiguration(config);

                using var diagnosticsService = InitializeDiagnosticsService(workspace, config);
                using var configForm = new ConfigForm(workspace, store, displayService, migrator);
                using var trayMenu = new TrayMenuManager(
                    orchestrator,
                    () => LoadConfigurationAsync(store, migrator),
                    ApplyConfiguration,
                    telemetry.LogDirectory);
                Application.Run(configForm);

                orchestrator.StopAsync().GetAwaiter().GetResult();

                return 0;
            }
            finally
            {
                displayService?.Dispose();
            }
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

    private static GeneralConfig LoadConfiguration(JsonStore<GeneralConfig> store, ConfigMigrator migrator)
        => LoadConfigurationAsync(store, migrator).GetAwaiter().GetResult();

    private static async Task<GeneralConfig> LoadConfigurationAsync(JsonStore<GeneralConfig> store, ConfigMigrator migrator)
    {
        try
        {
            var config = await store.LoadAsync().ConfigureAwait(false);
            if (config is null)
            {
                return new GeneralConfig();
            }

            return migrator.Migrate(config);
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

        if (config.Monitors.Count == 0)
        {
            return Array.Empty<MonitorInfo>();
        }

        var snapshot = config.Monitors.ToList();
        return new ReadOnlyCollection<MonitorInfo>(snapshot);
    }

    private static IDisplayService? CreateDisplayService(ITelemetry telemetry)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return new DisplayService(telemetry);
    }

    private static DiagnosticsService? InitializeDiagnosticsService(ConfiguratorWorkspace workspace, GeneralConfig config)
    {
        if (!HttpListener.IsSupported)
        {
            return null;
        }

        try
        {
            var service = new DiagnosticsService(() => BuildDiagnosticsReport(workspace, config));
            service.Start();
            return service;
        }
        catch (HttpListenerException ex)
        {
            Log.Warning(ex, "Falha ao iniciar o serviço de diagnósticos HTTP.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Erro inesperado ao iniciar o serviço de diagnósticos.");
        }

        return null;
    }

    private static DiagnosticsService.DiagnosticsReport BuildDiagnosticsReport(ConfiguratorWorkspace workspace, GeneralConfig config)
    {
        var monitorsActive = workspace.Monitors?.Count ?? 0;
        var cyclesRunning = CountActiveCycleItems(config?.Cycle);
        var watchdogsActive = CountActiveWatchdogs(workspace);
        var status = monitorsActive > 0 ? "Healthy" : "Degraded";

        return new DiagnosticsService.DiagnosticsReport
        {
            Status = status,
            MonitorsActive = monitorsActive,
            PreviewFps = 0,
            CyclesRunning = cyclesRunning,
            WatchdogsActive = watchdogsActive,
        };
    }

    private static int CountActiveCycleItems(CycleConfig? cycle)
    {
        if (cycle is not { Enabled: true })
        {
            return 0;
        }

        var items = cycle.Items ?? Array.Empty<CycleItem>();
        var active = 0;

        foreach (var item in items)
        {
            if (item is { Enabled: true })
            {
                active++;
            }
        }

        return active;
    }

    private static int CountActiveWatchdogs(ConfiguratorWorkspace workspace)
    {
        var count = 0;

        foreach (var app in workspace.Applications)
        {
            if (app?.Watchdog?.Enabled != false)
            {
                count++;
            }
        }

        foreach (var site in workspace.Sites)
        {
            if (site?.Watchdog?.Enabled != false)
            {
                count++;
            }
        }

        return count;
    }
}
