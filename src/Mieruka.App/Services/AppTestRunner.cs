#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Serilog;

namespace Mieruka.App.Services;

public interface IMonitorSelectionProvider
{
    IReadOnlyList<MonitorInfo> GetAvailableMonitors();

    string? SelectedMonitorId { get; }
}

public sealed class AppTestRunner
{
    private readonly IMonitorSelectionProvider _monitorProvider;
    private readonly IAppRunner _appRunner;
    private readonly ILogger _logger = Log.ForContext<AppTestRunner>();

    public AppTestRunner(IMonitorSelectionProvider monitorProvider, IAppRunner? appRunner = null)
    {
        _monitorProvider = monitorProvider ?? throw new ArgumentNullException(nameof(monitorProvider));
        _appRunner = appRunner ?? new AppRunner();
    }

    public async Task RunTestAsync(AppConfig programConfig, IWin32Window owner)
    {
        ArgumentNullException.ThrowIfNull(programConfig);
        ArgumentNullException.ThrowIfNull(owner);

        if (!OperatingSystem.IsWindows())
        {
            MessageBox.Show(
                owner,
                "O teste de posicionamento está disponível apenas no Windows.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var executablePath = programConfig.ExecutablePath?.Trim();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            MessageBox.Show(
                owner,
                "Informe um executável válido antes de executar o teste.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(executablePath))
        {
            MessageBox.Show(
                owner,
                "Executável não encontrado. Ajuste o caminho antes de executar o teste.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var monitors = _monitorProvider.GetAvailableMonitors() ?? Array.Empty<MonitorInfo>();
        if (monitors.Count == 0)
        {
            MessageBox.Show(
                owner,
                "Nenhum monitor disponível para o teste.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var monitor = ResolveMonitor(monitors, programConfig.Window, _monitorProvider.SelectedMonitorId);
        if (monitor is null)
        {
            MessageBox.Show(
                owner,
                "Selecione um monitor para testar a posição.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var bounds = WindowPlacementHelper.ResolveBounds(programConfig.Window, monitor);

        try
        {
            await _appRunner.RunAndPositionAsync(programConfig, monitor, bounds).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Falha ao executar o aplicativo real durante o teste.");
            MessageBox.Show(
                owner,
                $"Não foi possível executar o aplicativo real: {ex.Message}",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static MonitorInfo? ResolveMonitor(
        IReadOnlyList<MonitorInfo> monitors,
        WindowConfig window,
        string? selectedMonitorId)
    {
        var preferred = TryFindSelectedMonitor(monitors, selectedMonitorId);
        if (preferred is not null)
        {
            return preferred;
        }

        return WindowPlacementHelper.ResolveMonitor(displayService: null, monitors, window);
    }

    private static MonitorInfo? TryFindSelectedMonitor(
        IReadOnlyList<MonitorInfo> monitors,
        string? selectedMonitorId)
    {
        if (string.IsNullOrWhiteSpace(selectedMonitorId))
        {
            return null;
        }

        var normalizedSelection = MonitorIdentifier.Normalize(selectedMonitorId);

        foreach (var monitor in monitors)
        {
            if (monitor is null)
            {
                continue;
            }

            var monitorId = MonitorIdentifier.Normalize(MonitorIdentifier.Create(monitor));

            if (string.Equals(monitorId, normalizedSelection, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(MonitorIdentifier.Normalize(monitor.StableId), normalizedSelection, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }
}
