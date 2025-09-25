#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Services;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : Form
{
    private static readonly TimeSpan WindowTestTimeout = TimeSpan.FromSeconds(5);

    private readonly BindingList<SiteConfig> _sites;
    private readonly ProgramaConfig? _original;
    private readonly List<MonitorInfo> _monitors;
    private readonly string? _preferredMonitorId;
    private MonitorPreviewHost? _monitorPreviewHost;
    private MonitorInfo? _selectedMonitorInfo;
    private string? _selectedMonitorId;
    private bool _suppressMonitorComboEvents;

    public AppEditorForm(ProgramaConfig? programa = null, IReadOnlyList<MonitorInfo>? monitors = null, string? selectedMonitorId = null)
    {
        InitializeComponent();

        _ = tabEditor ?? throw new InvalidOperationException("O TabControl do editor não foi carregado.");
        var salvar = btnSalvar ?? throw new InvalidOperationException("O botão Salvar não foi carregado.");
        _ = btnCancelar ?? throw new InvalidOperationException("O botão Cancelar não foi carregado.");
        var sitesControl = sitesEditorControl ?? throw new InvalidOperationException("O controle de sites não foi carregado.");
        var appsTab = appsTabControl ?? throw new InvalidOperationException("A aba de aplicativos não foi carregada.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi configurado.");

        AcceptButton = salvar;
        CancelButton = btnCancelar;

        _monitors = monitors?.ToList() ?? new List<MonitorInfo>();
        _preferredMonitorId = selectedMonitorId;

        _sites = new BindingList<SiteConfig>();
        sitesControl.Sites = _sites;
        sitesControl.AddRequested += SitesEditorControl_AddRequested;
        sitesControl.RemoveRequested += SitesEditorControl_RemoveRequested;
        sitesControl.CloneRequested += SitesEditorControl_CloneRequested;

        appsTab.ExecutableChosen += AppsTab_ExecutableChosen;
        appsTab.ExecutableCleared += AppsTab_ExecutableCleared;
        appsTab.ArgumentsChanged += AppsTab_ArgumentsChanged;
        appsTab.OpenRequested += AppsTab_OpenRequestedAsync;
        appsTab.TestRequested += AppsTab_TestRequestedAsync;
        _ = appsTab.LoadInstalledAppsAsync();

        cboMonitores.SelectedIndexChanged += cboMonitores_SelectedIndexChanged;
        PopulateMonitorCombo(programa);

        if (programa is not null)
        {
            _original = programa;
            CarregarPrograma(programa);
        }
        else
        {
            chkAutoStart.Checked = true;
            chkJanelaTelaCheia.Checked = true;
            UpdateMonitorPreview();
        }

        appsTab.ExecutablePath = txtExecutavel.Text;
        appsTab.Arguments = txtArgumentos.Text;
    }

    public ProgramaConfig? Resultado { get; private set; }

    public BindingList<SiteConfig> ResultadoSites => new(_sites.Select(site => site with { }).ToList());

    public string? SelectedMonitorId => _selectedMonitorId;

    private void CarregarPrograma(ProgramaConfig programa)
    {
        txtId.Text = programa.Id;
        txtExecutavel.Text = programa.ExecutablePath;
        txtArgumentos.Text = programa.Arguments ?? string.Empty;
        chkAutoStart.Checked = programa.AutoStart;

        var janela = programa.Window ?? new WindowConfig();
        chkJanelaTelaCheia.Checked = janela.FullScreen;
        if (!janela.FullScreen)
        {
            if (janela.X is int x)
            {
                nudJanelaX.Value = AjustarRange(nudJanelaX, x);
            }
            if (janela.Y is int y)
            {
                nudJanelaY.Value = AjustarRange(nudJanelaY, y);
            }
            if (janela.Width is int largura)
            {
                nudJanelaLargura.Value = AjustarRange(nudJanelaLargura, largura);
            }
            if (janela.Height is int altura)
            {
                nudJanelaAltura.Value = AjustarRange(nudJanelaAltura, altura);
            }
        }

        appsTabControl!.ExecutablePath = programa.ExecutablePath;
        appsTabControl.Arguments = programa.Arguments ?? string.Empty;
    }

    private static decimal AjustarRange(NumericUpDown control, int value)
    {
        var decimalValue = (decimal)value;
        if (decimalValue < control.Minimum)
        {
            return control.Minimum;
        }

        if (decimalValue > control.Maximum)
        {
            return control.Maximum;
        }

        return decimalValue;
    }

    private void SitesEditorControl_AddRequested(object? sender, EventArgs e)
    {
        var novoId = GerarIdSite();
        var site = new SiteConfig
        {
            Id = novoId,
            Url = "https://exemplo.com",
        };

        _sites.Add(site);
        sitesEditorControl?.SelectSite(site);
    }

    private void SitesEditorControl_RemoveRequested(object? sender, EventArgs e)
    {
        var site = sitesEditorControl?.SelectedSite;
        if (site is null)
        {
            return;
        }

        _sites.Remove(site);
    }

    private void SitesEditorControl_CloneRequested(object? sender, EventArgs e)
    {
        var site = sitesEditorControl?.SelectedSite;
        if (site is null)
        {
            return;
        }

        var clone = site with { Id = GerarIdSite(site.Id + "_clone") };
        _sites.Add(clone);
        sitesEditorControl?.SelectSite(clone);
    }

    private string GerarIdSite(string? baseId = null)
    {
        var prefixo = string.IsNullOrWhiteSpace(baseId) ? "site" : baseId;
        var contador = 1;

        var candidato = prefixo;
        while (_sites.Any(s => string.Equals(s.Id, candidato, StringComparison.OrdinalIgnoreCase)))
        {
            candidato = $"{prefixo}_{contador++}";
        }

        return candidato;
    }

    private void PopulateMonitorCombo(ProgramaConfig? programa)
    {
        if (cboMonitores is null)
        {
            return;
        }

        _suppressMonitorComboEvents = true;

        try
        {
            cboMonitores.Items.Clear();

            if (_monitors.Count == 0)
            {
                cboMonitores.Items.Add(MonitorOption.Empty());
                cboMonitores.SelectedIndex = 0;
                _selectedMonitorInfo = null;
                _selectedMonitorId = null;
            }
            else
            {
                foreach (var monitor in _monitors)
                {
                    var monitorId = MonitorIdentifier.Create(monitor);
                    var displayName = LayoutHelpers.GetMonitorDisplayName(monitor);
                    cboMonitores.Items.Add(new MonitorOption(monitorId, monitor, displayName));
                }

                var candidates = new[]
                {
                    _preferredMonitorId,
                    programa?.TargetMonitorStableId,
                    MonitorIdentifier.Create(programa?.Window?.Monitor),
                };

                var selectionApplied = false;
                foreach (var candidate in candidates)
                {
                    if (SelectMonitorById(candidate))
                    {
                        selectionApplied = true;
                        break;
                    }
                }

                if (!selectionApplied && cboMonitores.Items.Count > 0)
                {
                    cboMonitores.SelectedIndex = 0;
                }
            }
        }
        finally
        {
            _suppressMonitorComboEvents = false;
        }

        UpdateMonitorPreview();
    }

    private bool SelectMonitorById(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || cboMonitores is null)
        {
            return false;
        }

        for (var index = 0; index < cboMonitores.Items.Count; index++)
        {
            if (cboMonitores.Items[index] is not MonitorOption option || option.MonitorId is null)
            {
                continue;
            }

            if (string.Equals(option.MonitorId, identifier, StringComparison.OrdinalIgnoreCase))
            {
                cboMonitores.SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    private void cboMonitores_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressMonitorComboEvents)
        {
            return;
        }

        UpdateMonitorPreview();
    }

    private void UpdateMonitorPreview()
    {
        _monitorPreviewHost?.Dispose();
        _monitorPreviewHost = null;
        _selectedMonitorInfo = null;
        _selectedMonitorId = null;

        if (cboMonitores?.SelectedItem is not MonitorOption option || option.Monitor is null || option.MonitorId is null || picMonitorPreview is null)
        {
            if (picMonitorPreview is not null)
            {
                picMonitorPreview.Image = null;
            }

            return;
        }

        _selectedMonitorInfo = option.Monitor;
        _selectedMonitorId = option.MonitorId;
        _monitorPreviewHost = new MonitorPreviewHost(option.MonitorId, picMonitorPreview);

        try
        {
            _monitorPreviewHost.Start();
        }
        catch
        {
            // The preview is best-effort only; ignore failures.
        }
    }

    private MonitorInfo? GetSelectedMonitor()
    {
        if (_selectedMonitorInfo is not null)
        {
            return _selectedMonitorInfo;
        }

        return cboMonitores?.SelectedItem is MonitorOption option ? option.Monitor : null;
    }

    private static WindowConfig ClampWindowBounds(WindowConfig window, MonitorInfo monitor)
    {
        var width = window.Width;
        var height = window.Height;
        var x = window.X;
        var y = window.Y;

        if (width is int w && monitor.Width > 0)
        {
            width = Math.Clamp(w, 1, monitor.Width);
        }

        if (height is int h && monitor.Height > 0)
        {
            height = Math.Clamp(h, 1, monitor.Height);
        }

        if (x is int posX && width is int wValue && monitor.Width > 0)
        {
            var maxX = Math.Max(0, monitor.Width - wValue);
            x = Math.Clamp(posX, 0, maxX);
        }

        if (y is int posY && height is int hValue && monitor.Height > 0)
        {
            var maxY = Math.Max(0, monitor.Height - hValue);
            y = Math.Clamp(posY, 0, maxY);
        }

        return window with
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };
    }

    private async void btnTestarJanela_Click(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            MessageBox.Show(
                this,
                "O teste de posicionamento está disponível apenas no Windows.",
                "Teste de janela",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            MessageBox.Show(
                this,
                "Selecione um monitor para testar a posição.",
                "Teste de janela",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var window = BuildWindowConfigurationFromInputs();
        if (!window.FullScreen)
        {
            window = ClampWindowBounds(window, monitor);
        }

        window = window with { Monitor = monitor.Key };

        var executablePath = txtExecutavel.Text.Trim();
        var arguments = string.IsNullOrWhiteSpace(txtArgumentos.Text) ? null : txtArgumentos.Text;
        var hasExecutable = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);
        var button = btnTestarJanela;

        if (button is not null)
        {
            button.Enabled = false;
        }

        try
        {
            if (hasExecutable)
            {
                var existingProcess = FindRunningProcess(executablePath);
                if (existingProcess is not null)
                {
                    try
                    {
                        await PositionExistingProcessAsync(existingProcess, monitor, window).ConfigureAwait(true);
                    }
                    finally
                    {
                        existingProcess.Dispose();
                    }
                }
                else
                {
                    await LaunchAndPositionProcessAsync(executablePath, arguments, monitor, window).ConfigureAwait(true);
                }
            }
            else
            {
                await LaunchDummyWindowAsync(monitor, window).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível testar a posição: {ex.Message}",
                "Teste de janela",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (button is not null)
            {
                button.Enabled = true;
            }
        }
    }

    private WindowConfig BuildWindowConfigurationFromInputs()
    {
        if (chkJanelaTelaCheia.Checked)
        {
            return new WindowConfig { FullScreen = true };
        }

        return new WindowConfig
        {
            FullScreen = false,
            X = (int)nudJanelaX.Value,
            Y = (int)nudJanelaY.Value,
            Width = (int)nudJanelaLargura.Value,
            Height = (int)nudJanelaAltura.Value,
        };
    }

    private async Task LaunchAndPositionProcessAsync(
        string executablePath,
        string? arguments,
        MonitorInfo monitor,
        WindowConfig window)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        var workingDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Não foi possível iniciar o processo para teste.");
        }

        try
        {
            var handle = await WindowWaiter.WaitForMainWindowAsync(process, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
            ApplyWindowPosition(handle, monitor, window);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void LaunchExecutable(string executablePath, string? arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        var workingDirectory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Não foi possível iniciar o aplicativo selecionado.");
        }
    }

    private async Task PositionExistingProcessAsync(Process process, MonitorInfo monitor, WindowConfig window)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException("O processo selecionado já foi encerrado.");
        }

        process.Refresh();
        var handle = process.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            handle = await WindowWaiter.WaitForMainWindowAsync(process, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
        }

        ApplyWindowPosition(handle, monitor, window);
    }

    private async Task LaunchDummyWindowAsync(MonitorInfo monitor, WindowConfig window)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "notepad.exe",
            UseShellExecute = true,
        };

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Não foi possível iniciar a janela de teste.");
        }

        try
        {
            var handle = await WindowWaiter.WaitForMainWindowAsync(process, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
            ApplyWindowPosition(handle, monitor, window);
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit((int)WindowTestTimeout.TotalMilliseconds))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                // Ignore failures while closing the dummy window.
            }

            process.Dispose();
        }
    }

    private static void ApplyWindowPosition(IntPtr handle, MonitorInfo monitor, WindowConfig window)
    {
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("A janela de destino não foi localizada.");
        }

        var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);
        WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
    }

    private static Process? FindRunningProcess(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        try
        {
            var normalized = Path.GetFullPath(executablePath);
            var processName = Path.GetFileNameWithoutExtension(normalized);
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            var candidates = Process.GetProcessesByName(processName);
            Process? match = null;

            foreach (var candidate in candidates)
            {
                try
                {
                    var module = candidate.MainModule;
                    var candidatePath = module?.FileName;
                    if (!string.IsNullOrWhiteSpace(candidatePath) &&
                        string.Equals(Path.GetFullPath(candidatePath), normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        match = candidate;
                        break;
                    }
                }
                catch (Win32Exception)
                {
                    // Ignore processes without access rights.
                }
                catch (InvalidOperationException)
                {
                    // Ignore processes that exited while enumerating.
                }
            }

            foreach (var candidate in candidates)
            {
                if (!ReferenceEquals(candidate, match))
                {
                    candidate.Dispose();
                }
            }

            if (match is not null)
            {
                return match;
            }
        }
        catch
        {
            // Swallow exceptions when enumerating running processes.
        }

        return null;
    }

    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        if (!ValidarCampos())
        {
            DialogResult = DialogResult.None;
            return;
        }

        Resultado = ConstruirPrograma();
        DialogResult = DialogResult.OK;
        Close();
    }

    private ProgramaConfig ConstruirPrograma()
    {
        var id = txtId.Text.Trim();
        var executavel = txtExecutavel.Text.Trim();
        var argumentos = string.IsNullOrWhiteSpace(txtArgumentos.Text) ? null : txtArgumentos.Text.Trim();

        var janela = chkJanelaTelaCheia.Checked
            ? new WindowConfig { FullScreen = true }
            : new WindowConfig
            {
                FullScreen = false,
                X = (int)nudJanelaX.Value,
                Y = (int)nudJanelaY.Value,
                Width = (int)nudJanelaLargura.Value,
                Height = (int)nudJanelaAltura.Value,
            };

        var monitorInfo = GetSelectedMonitor();
        if (monitorInfo is not null)
        {
            if (!janela.FullScreen)
            {
                janela = ClampWindowBounds(janela, monitorInfo);
            }

            janela = janela with { Monitor = monitorInfo.Key };
        }

        return (_original ?? new ProgramaConfig()) with
        {
            Id = id,
            ExecutablePath = executavel,
            Arguments = argumentos,
            AutoStart = chkAutoStart.Checked,
            Window = janela,
            TargetMonitorStableId = monitorInfo?.StableId ?? string.Empty,
        };
    }

    private bool ValidarCampos()
    {
        var valido = true;

        if (string.IsNullOrWhiteSpace(txtId.Text))
        {
            errorProvider.SetError(txtId, "Informe um identificador.");
            valido = false;
        }
        else
        {
            errorProvider.SetError(txtId, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(txtExecutavel.Text))
        {
            errorProvider.SetError(txtExecutavel, "Informe o executável.");
            valido = false;
        }
        else if (!File.Exists(txtExecutavel.Text))
        {
            errorProvider.SetError(txtExecutavel, "Executável não encontrado.");
            valido = false;
        }
        else
        {
            errorProvider.SetError(txtExecutavel, string.Empty);
        }

        return valido;
    }

    private void btnCancelar_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _monitorPreviewHost?.Dispose();
        _monitorPreviewHost = null;
    }

    private void AppsTab_ExecutableChosen(object? sender, AppSelectionEventArgs e)
    {
        txtExecutavel.Text = e.ExecutablePath;
        appsTabControl!.ExecutablePath = e.ExecutablePath;
    }

    private void AppsTab_ExecutableCleared(object? sender, EventArgs e)
    {
        txtExecutavel.Text = string.Empty;
        appsTabControl!.ExecutablePath = string.Empty;
    }

    private void AppsTab_ArgumentsChanged(object? sender, string e)
    {
        txtArgumentos.Text = e;
    }

    private async Task AppsTab_OpenRequestedAsync(object? sender, AppExecutionRequestEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.ExecutablePath) || !File.Exists(e.ExecutablePath))
        {
            MessageBox.Show(
                this,
                "Selecione um executável válido para abrir.",
                "Abrir aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await Task.Run(() => LaunchExecutable(e.ExecutablePath, e.Arguments)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível abrir o aplicativo selecionado: {ex.Message}",
                "Abrir aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task AppsTab_TestRequestedAsync(object? sender, AppExecutionRequestEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            MessageBox.Show(
                this,
                "O teste de posicionamento está disponível apenas no Windows.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            MessageBox.Show(
                this,
                "Selecione um monitor para testar a posição.",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var window = BuildWindowConfigurationFromInputs();
        if (!window.FullScreen)
        {
            window = ClampWindowBounds(window, monitor);
        }

        window = window with { Monitor = monitor.Key };

        var executablePath = e.ExecutablePath;
        var arguments = e.Arguments;
        var hasExecutable = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);

        try
        {
            if (hasExecutable)
            {
                var existingProcess = FindRunningProcess(executablePath);
                if (existingProcess is not null)
                {
                    try
                    {
                        await PositionExistingProcessAsync(existingProcess, monitor, window).ConfigureAwait(true);
                    }
                    finally
                    {
                        existingProcess.Dispose();
                    }
                }
                else
                {
                    await LaunchAndPositionProcessAsync(executablePath, arguments, monitor, window).ConfigureAwait(true);
                }
            }
            else
            {
                await LaunchDummyWindowAsync(monitor, window).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível testar a posição: {ex.Message}",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private sealed class MonitorOption
    {
        public MonitorOption(string? monitorId, MonitorInfo? monitor, string displayName)
        {
            MonitorId = monitorId;
            Monitor = monitor;
            DisplayName = displayName;
        }

        public string? MonitorId { get; }

        public MonitorInfo? Monitor { get; }

        public string DisplayName { get; }

        public static MonitorOption Empty()
            => new(null, null, "Nenhum monitor disponível");

        public override string ToString() => DisplayName;
    }
}
