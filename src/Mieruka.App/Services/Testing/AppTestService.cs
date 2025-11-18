using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mieruka.App.Config;
using Mieruka.Core.Monitors;
using Mieruka.Core.Services;
using Serilog;

namespace Mieruka.App.Services.Testing;

internal sealed class AppTestService
{
    private readonly ConfiguratorWorkspace _workspace;
    private readonly IDisplayService? _displayService;
    private readonly ILogger _logger = Log.ForContext<AppTestService>();

    public AppTestService(ConfiguratorWorkspace workspace, IDisplayService? displayService)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _displayService = displayService;
    }

    public async Task<TestRunResult> TestAsync(AppConfig app, string? selectedMonitorStableId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(app);

        var monitor = WindowPlacementHelper.ResolveTargetMonitor(app, selectedMonitorStableId, _workspace.Monitors, _displayService);
        var zone = WindowPlacementHelper.ResolveTargetZone(monitor, app.TargetZonePresetId, app.Window, _workspace.ZonePresets);

        if (!File.Exists(app.ExecutablePath))
        {
            return new TestRunResult(false, false, monitor, zone, $"O executável '{app.ExecutablePath}' não foi encontrado.");
        }

        try
        {
            var startInfo = BuildStartInfo(app);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new TestRunResult(false, false, monitor, zone, "Process.Start retornou nulo ao iniciar o aplicativo.");
            }

            var topMost = app.Window.AlwaysOnTop || (zone.WidthPercentage >= 99.5 && zone.HeightPercentage >= 99.5);
            await WindowPlacementHelper
                .ForcePlaceProcessWindowAsync(process, monitor, zone, topMost, ct)
                .ConfigureAwait(false);

            process.Refresh();
            var hasWindow = process.MainWindowHandle != IntPtr.Zero;
            return new TestRunResult(true, hasWindow, monitor, zone, hasWindow ? null : "O aplicativo foi iniciado, mas a janela principal não foi localizada.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Falha ao testar aplicativo {AppId}.", app.Id);
            return new TestRunResult(false, false, monitor, zone, $"Falha ao iniciar o aplicativo: {ex.Message}");
        }
    }

    private static ProcessStartInfo BuildStartInfo(AppConfig app)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = app.ExecutablePath,
            Arguments = app.Arguments ?? string.Empty,
            UseShellExecute = false,
        };

        foreach (var pair in app.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }
}
