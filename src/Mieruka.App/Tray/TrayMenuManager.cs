using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Models;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Tray;

/// <summary>
/// Manages the application tray icon and exposes orchestrator operations to the user.
/// </summary>
internal sealed class TrayMenuManager : IDisposable
{
    private readonly Orchestrator _orchestrator;
    private readonly Func<Task<GeneralConfig>> _loadConfigurationAsync;
    private readonly Action<GeneralConfig> _applyConfiguration;
    private readonly string _logDirectory;
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly WinForms.ContextMenuStrip _contextMenu;
    private readonly WinForms.ToolStripMenuItem _statusItem;
    private readonly WinForms.ToolStripMenuItem _toggleItem;
    private readonly WinForms.ToolStripMenuItem _reloadItem;
    private readonly WinForms.ToolStripMenuItem _openLogsItem;
    private readonly WinForms.ToolStripMenuItem _exitItem;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private bool _disposed;
    private volatile bool _operationInProgress;
    private OrchestratorState? _lastNotifiedState;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayMenuManager"/> class.
    /// </summary>
    /// <param name="orchestrator">Orchestrator controlled by the tray menu.</param>
    /// <param name="loadConfigurationAsync">Delegate used to retrieve the latest configuration.</param>
    /// <param name="applyConfiguration">Delegate used to apply configurations to background services.</param>
    /// <param name="logDirectory">Directory where telemetry logs are stored.</param>
    public TrayMenuManager(
        Orchestrator orchestrator,
        Func<Task<GeneralConfig>> loadConfigurationAsync,
        Action<GeneralConfig> applyConfiguration,
        string logDirectory)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _loadConfigurationAsync = loadConfigurationAsync ?? throw new ArgumentNullException(nameof(loadConfigurationAsync));
        _applyConfiguration = applyConfiguration ?? throw new ArgumentNullException(nameof(applyConfiguration));
        if (string.IsNullOrWhiteSpace(logDirectory))
        {
            throw new ArgumentException("Log directory must be provided.", nameof(logDirectory));
        }

        _logDirectory = logDirectory;

        _contextMenu = new WinForms.ContextMenuStrip();
        _ = _contextMenu.Handle; // Force handle creation for cross-thread updates.

        _statusItem = new WinForms.ToolStripMenuItem
        {
            Enabled = false,
        };

        _toggleItem = new WinForms.ToolStripMenuItem();
        _toggleItem.Click += OnToggleClick;

        _reloadItem = new WinForms.ToolStripMenuItem("Reload Config");
        _reloadItem.Click += OnReloadClick;

        _openLogsItem = new WinForms.ToolStripMenuItem("Open Logs");
        _openLogsItem.Click += OnOpenLogsClick;

        _exitItem = new WinForms.ToolStripMenuItem("Exit");
        _exitItem.Click += OnExitClick;

        _contextMenu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _statusItem,
            new WinForms.ToolStripSeparator(),
            _toggleItem,
            _reloadItem,
            _openLogsItem,
            new WinForms.ToolStripSeparator(),
            _exitItem,
        });

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            ContextMenuStrip = _contextMenu,
        };

        _orchestrator.StateChanged += OnOrchestratorStateChanged;
        UpdateMenuState();
    }

    private static Drawing.Icon LoadAppIcon()
    {
        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "Properties", "app.ico");
            if (File.Exists(icoPath))
            {
                return new Drawing.Icon(icoPath);
            }
        }
        catch
        {
            // Fall through to default icon.
        }

        return SystemIcons.Application;
    }

    /// <summary>
    /// Releases tray resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _orchestrator.StateChanged -= OnOrchestratorStateChanged;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _operationGate.Dispose();
    }

    private void OnOrchestratorStateChanged(object? sender, OrchestratorStateChangedEventArgs e)
    {
        UpdateMenuState();
        ShowStateNotification(e);
    }

    private void ShowStateNotification(OrchestratorStateChangedEventArgs e)
    {
        var newState = _orchestrator.State;
        if (newState == _lastNotifiedState)
        {
            return;
        }

        _lastNotifiedState = newState;

        switch (newState)
        {
            case OrchestratorState.Running:
                ShowNotification("MRK Configurator", "Orchestrator iniciado.", WinForms.ToolTipIcon.Info);
                break;
            case OrchestratorState.Recovering:
                ShowNotification("MRK Configurator", "Recuperando de falha...", WinForms.ToolTipIcon.Warning);
                break;
            case OrchestratorState.Ready when e.PreviousState is OrchestratorState.Running or OrchestratorState.Recovering:
                ShowNotification("MRK Configurator", "Orchestrator parado.", WinForms.ToolTipIcon.Info);
                break;
        }
    }

    private void ShowNotification(string title, string text, WinForms.ToolTipIcon icon)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, text, icon);
        }
        catch
        {
            // Ignore failures in balloon tip display.
        }
    }

    private async void OnToggleClick(object? sender, EventArgs e)
    {
        try
        {
            if (_operationInProgress)
            {
                return;
            }

            if (_orchestrator.State is OrchestratorState.Running or OrchestratorState.Recovering)
            {
                await ExecuteWithLockAsync(() => _orchestrator.StopAsync());
            }
            else
            {
                await ExecuteWithLockAsync(() => _orchestrator.StartAsync());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] OnToggleClick: {ex.Message}");
        }
    }

    private async void OnReloadClick(object? sender, EventArgs e)
    {
        try
        {
            if (_operationInProgress)
            {
                return;
            }

            await ExecuteWithLockAsync(async () =>
            {
                var config = await _loadConfigurationAsync();
                _applyConfiguration(config);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] OnReloadClick: {ex.Message}");
        }
    }

    private void OnOpenLogsClick(object? sender, EventArgs e)
    {
        try
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _logDirectory,
                UseShellExecute = true,
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Unable to open log directory: {ex.Message}",
                "Tray Menu",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private void OnExitClick(object? sender, EventArgs e)
        => WinForms.Application.Exit();

    private async Task ExecuteWithLockAsync(Func<Task> operation)
    {
        await _operationGate.WaitAsync();

        try
        {
            _operationInProgress = true;
            UpdateMenuState();

            _reloadItem.Enabled = false;

            await operation();
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                $"Operation failed: {ex.Message}",
                "Tray Menu",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            _operationInProgress = false;
            _reloadItem.Enabled = true;
            _operationGate.Release();
            UpdateMenuState();
        }
    }

    private void UpdateMenuState()
    {
        if (_contextMenu.IsHandleCreated && _contextMenu.InvokeRequired)
        {
            _contextMenu.BeginInvoke(new WinForms.MethodInvoker(UpdateMenuState));
            return;
        }

        var state = _orchestrator.State;
        var statusText = state switch
        {
            OrchestratorState.Running => "Running",
            OrchestratorState.Recovering => "Recovering",
            _ => "Paused",
        };

        _statusItem.Text = $"Status: {statusText}";

        var isRunning = state is OrchestratorState.Running or OrchestratorState.Recovering;
        _toggleItem.Text = isRunning ? "Stop Orchestrator" : "Start Orchestrator";
        _toggleItem.Enabled = !_operationInProgress;

        _reloadItem.Enabled = !_operationInProgress;
        _openLogsItem.Enabled = !_operationInProgress;

        var tooltipText = $"MRK Configurator ({statusText})";
        _notifyIcon.Text = tooltipText.Length <= 63 ? tooltipText : tooltipText.Substring(0, 63);
    }
}
