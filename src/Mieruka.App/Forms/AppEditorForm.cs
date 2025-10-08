#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Services;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using Mieruka.Core.Services;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : Form
{
    private static readonly TimeSpan WindowTestTimeout = TimeSpan.FromSeconds(5);
    private const int EnumCurrentSettings = -1;

    private readonly BindingList<SiteConfig> _sites;
    private readonly ProgramaConfig? _original;
    private readonly IReadOnlyList<MonitorInfo>? _providedMonitors;
    private readonly List<MonitorInfo> _monitors;
    private readonly string? _preferredMonitorId;
    private readonly IAppRunner _appRunner;
    private MonitorInfo? _selectedMonitorInfo;
    private string? _selectedMonitorId;
    private bool _suppressMonitorComboEvents;

    public AppEditorForm(
        ProgramaConfig? programa = null,
        IReadOnlyList<MonitorInfo>? monitors = null,
        string? selectedMonitorId = null,
        IAppRunner? appRunner = null)
    {
        InitializeComponent();

        _ = tabEditor ?? throw new InvalidOperationException("O TabControl do editor não foi carregado.");
        var salvar = btnSalvar ?? throw new InvalidOperationException("O botão Salvar não foi carregado.");
        _ = btnCancelar ?? throw new InvalidOperationException("O botão Cancelar não foi carregado.");
        var sitesControl = sitesEditorControl ?? throw new InvalidOperationException("O controle de sites não foi carregado.");
        var appsTab = appsTabControl ?? throw new InvalidOperationException("A aba de aplicativos não foi carregada.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi configurado.");

        _ = tlpMonitorPreview ?? throw new InvalidOperationException("O painel de pré-visualização não foi configurado.");
        var previewControl = monitorPreviewDisplay ?? throw new InvalidOperationException("O controle de pré-visualização do monitor não foi configurado.");
        _ = lblMonitorCoordinates ?? throw new InvalidOperationException("O rótulo de coordenadas do monitor não foi configurado.");
        var janelaTab = tpJanela ?? throw new InvalidOperationException("A aba de janela não foi configurada.");

        AcceptButton = salvar;
        CancelButton = btnCancelar;

        _appRunner = appRunner ?? new AppRunner();
        _providedMonitors = monitors;
        _monitors = new List<MonitorInfo>();
        _preferredMonitorId = selectedMonitorId;

        RefreshMonitorSnapshot();

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

        txtExecutavel.TextChanged += (_, _) => UpdateExePreview();
        txtArgumentos.TextChanged += (_, _) => UpdateExePreview();

        cboMonitores.SelectedIndexChanged += cboMonitores_SelectedIndexChanged;
        PopulateMonitorCombo(programa);

        previewControl.MouseMovedInMonitorSpace += MonitorPreviewDisplay_MouseMovedInMonitorSpace;
        previewControl.MonitorMouseLeft += MonitorPreviewDisplay_MonitorMouseLeft;

        janelaTab.SizeChanged += (_, _) => AdjustMonitorPreviewWidth();

        chkJanelaTelaCheia.CheckedChanged += chkJanelaTelaCheia_CheckedChanged;

        AdjustMonitorPreviewWidth();
        UpdateWindowInputsState();
        UpdateMonitorCoordinateLabel(null);

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
        UpdateExePreview();
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

        UpdateWindowInputsState();
        UpdateMonitorPreview();
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

    private void RefreshMonitorSnapshot()
    {
        List<MonitorInfo> merged;

        try
        {
            var installed = DisplayService.GetMonitors();
            merged = installed.Count > 0
                ? MergeMonitors(installed, _providedMonitors, _monitors)
                : MergeMonitors(_providedMonitors, _monitors);
        }
        catch
        {
            merged = MergeMonitors(_providedMonitors, _monitors);
        }

        _monitors.Clear();
        _monitors.AddRange(merged);
    }

    private static List<MonitorInfo> MergeMonitors(params IEnumerable<MonitorInfo>?[] sources)
    {
        var result = new List<MonitorInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var monitor in source)
            {
                if (monitor is null)
                {
                    continue;
                }

                var key = GetMonitorKey(monitor);
                if (seen.Add(key))
                {
                    result.Add(monitor);
                }
            }
        }

        return result;
    }

    private static string GetMonitorKey(MonitorInfo monitor)
    {
        if (!string.IsNullOrWhiteSpace(monitor.StableId))
        {
            return monitor.StableId;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            return monitor.DeviceName;
        }

        if (!string.IsNullOrWhiteSpace(monitor.Key.DeviceId))
        {
            return monitor.Key.DeviceId;
        }

        var bounds = monitor.Bounds;
        return string.Join(
            '|',
            monitor.Key.AdapterLuidHigh.ToString("X8", CultureInfo.InvariantCulture),
            monitor.Key.AdapterLuidLow.ToString("X8", CultureInfo.InvariantCulture),
            monitor.Key.TargetId.ToString(CultureInfo.InvariantCulture),
            bounds.X.ToString(CultureInfo.InvariantCulture),
            bounds.Y.ToString(CultureInfo.InvariantCulture),
            bounds.Width.ToString(CultureInfo.InvariantCulture),
            bounds.Height.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatMonitorDisplayName(MonitorInfo monitor)
    {
        var deviceName = !string.IsNullOrWhiteSpace(monitor.DeviceName)
            ? monitor.DeviceName
            : (!string.IsNullOrWhiteSpace(monitor.Name) ? monitor.Name : "Monitor");

        var width = monitor.Width > 0 ? monitor.Width : monitor.Bounds.Width;
        var height = monitor.Height > 0 ? monitor.Height : monitor.Bounds.Height;
        var resolution = width > 0 && height > 0 ? $"{width}x{height}" : "?x?";

        var refresh = TryGetRefreshRate(monitor.DeviceName);
        var refreshText = refresh > 0 ? $"{refresh}Hz" : "?Hz";

        return $"{deviceName} {resolution} @ {refreshText}";
    }

    private void PopulateMonitorCombo(ProgramaConfig? programa)
    {
        if (cboMonitores is null)
        {
            return;
        }

        RefreshMonitorSnapshot();
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
                    var displayName = FormatMonitorDisplayName(monitor);
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
        var previousMonitor = _selectedMonitorInfo;
        var previousMonitorId = _selectedMonitorId;
        var previousWindow = BuildWindowConfigurationFromInputs();

        _selectedMonitorInfo = null;
        _selectedMonitorId = null;

        if (cboMonitores?.SelectedItem is not MonitorOption option || option.Monitor is null || option.MonitorId is null)
        {
            monitorPreviewDisplay?.Unbind();
            UpdateMonitorCoordinateLabel(null);
            return;
        }

        _selectedMonitorInfo = option.Monitor;
        _selectedMonitorId = option.MonitorId;

        if (previousMonitor is not null &&
            !string.Equals(previousMonitorId, option.MonitorId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyRelativeWindowToNewMonitor(previousWindow, previousMonitor, option.Monitor);
        }

        monitorPreviewDisplay?.Bind(option.Monitor);
        UpdateMonitorCoordinateLabel(null);
    }

    private void chkJanelaTelaCheia_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateWindowInputsState();

        _ = ClampWindowInputsToMonitor(null, allowFullScreen: true);
        InvalidateWindowPreviewOverlay();
    }

    private void UpdateWindowInputsState()
    {
        var enabled = !chkJanelaTelaCheia.Checked;

        if (nudJanelaX is not null)
        {
            nudJanelaX.Enabled = enabled;
        }

        if (nudJanelaY is not null)
        {
            nudJanelaY.Enabled = enabled;
        }

        if (nudJanelaLargura is not null)
        {
            nudJanelaLargura.Enabled = enabled;
        }

        if (nudJanelaAltura is not null)
        {
            nudJanelaAltura.Enabled = enabled;
        }
    }

    private void UpdateMonitorCoordinateLabel(Point? coordinates)
    {
        if (lblMonitorCoordinates is null)
        {
            return;
        }

        lblMonitorCoordinates.Text = coordinates is null
            ? "X=–, Y=–"
            : $"X={coordinates.Value.X}, Y={coordinates.Value.Y}";
    }

    private void MonitorPreviewDisplay_MouseMovedInMonitorSpace(object? sender, Point point)
    {
        UpdateMonitorCoordinateLabel(point);

        if (ClampWindowInputsToMonitor(point))
        {
            InvalidateWindowPreviewOverlay();
        }
    }

    private void MonitorPreviewDisplay_MonitorMouseLeft(object? sender, EventArgs e)
    {
        UpdateMonitorCoordinateLabel(null);

        _ = ClampWindowInputsToMonitor(null);
        InvalidateWindowPreviewOverlay();
    }

    private bool ClampWindowInputsToMonitor(Point? pointer, bool allowFullScreen = false)
    {
        if (!allowFullScreen && chkJanelaTelaCheia.Checked)
        {
            return false;
        }

        if (nudJanelaX is null ||
            nudJanelaY is null ||
            nudJanelaLargura is null ||
            nudJanelaAltura is null)
        {
            return false;
        }

        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            return false;
        }

        var changed = false;

        var width = (int)nudJanelaLargura.Value;
        if (monitor.Width > 0)
        {
            var clampedWidth = Math.Clamp(width, 1, monitor.Width);
            changed |= UpdateNumericControl(nudJanelaLargura, clampedWidth);
            width = clampedWidth;
        }

        var height = (int)nudJanelaAltura.Value;
        if (monitor.Height > 0)
        {
            var clampedHeight = Math.Clamp(height, 1, monitor.Height);
            changed |= UpdateNumericControl(nudJanelaAltura, clampedHeight);
            height = clampedHeight;
        }

        if (pointer is Point target)
        {
            var targetX = target.X;
            if (monitor.Width > 0)
            {
                var maxX = Math.Max(0, monitor.Width - width);
                targetX = Math.Clamp(targetX, 0, maxX);
            }

            var targetY = target.Y;
            if (monitor.Height > 0)
            {
                var maxY = Math.Max(0, monitor.Height - height);
                targetY = Math.Clamp(targetY, 0, maxY);
            }

            changed |= UpdateNumericControl(nudJanelaX, targetX);
            changed |= UpdateNumericControl(nudJanelaY, targetY);
        }
        else
        {
            if (monitor.Width > 0)
            {
                var currentX = (int)nudJanelaX.Value;
                var maxX = Math.Max(0, monitor.Width - width);
                var clampedX = Math.Clamp(currentX, 0, maxX);
                changed |= UpdateNumericControl(nudJanelaX, clampedX);
            }

            if (monitor.Height > 0)
            {
                var currentY = (int)nudJanelaY.Value;
                var maxY = Math.Max(0, monitor.Height - height);
                var clampedY = Math.Clamp(currentY, 0, maxY);
                changed |= UpdateNumericControl(nudJanelaY, clampedY);
            }
        }

        return changed;
    }

    private static bool UpdateNumericControl(NumericUpDown control, int value)
    {
        var adjusted = AjustarRange(control, value);
        if (control.Value != adjusted)
        {
            control.Value = adjusted;
            return true;
        }

        return false;
    }

    private void InvalidateWindowPreviewOverlay()
    {
        monitorPreviewDisplay?.Invalidate();
        monitorPreviewDisplay?.Update();
    }

    private void AdjustMonitorPreviewWidth()
    {
        if (tlpMonitorPreview is null || tpJanela is null)
        {
            return;
        }

        var availableWidth = tpJanela.ClientSize.Width;
        if (availableWidth <= 0)
        {
            return;
        }

        var minimumWidth = tlpMonitorPreview.MinimumSize.Width > 0 ? tlpMonitorPreview.MinimumSize.Width : 420;
        if (availableWidth <= minimumWidth)
        {
            tlpMonitorPreview.Width = availableWidth;
            return;
        }

        var desired = Math.Max(minimumWidth, availableWidth / 2);
        var maxAllowed = Math.Max(minimumWidth, availableWidth - 320);
        var width = Math.Min(desired, maxAllowed);
        width = Math.Max(minimumWidth, Math.Min(width, availableWidth));
        tlpMonitorPreview.Width = width;
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

    private void ApplyRelativeWindowToNewMonitor(WindowConfig previousWindow, MonitorInfo previousMonitor, MonitorInfo newMonitor)
    {
        if (previousWindow.FullScreen)
        {
            return;
        }

        var zone = WindowPlacementHelper.CreateZoneFromWindow(previousWindow, previousMonitor);
        var relative = CalculateRelativeRectangle(zone, newMonitor);
        ApplyBoundsToInputs(relative);
    }

    private static Rectangle CalculateRelativeRectangle(WindowPlacementHelper.ZoneRect zone, MonitorInfo monitor)
    {
        var monitorWidth = Math.Max(1, monitor.Width);
        var monitorHeight = Math.Max(1, monitor.Height);

        var width = Math.Max(1, (int)Math.Round(monitorWidth * (zone.WidthPercentage / 100d), MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(monitorHeight * (zone.HeightPercentage / 100d), MidpointRounding.AwayFromZero));
        var x = (int)Math.Round(monitorWidth * (zone.LeftPercentage / 100d), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(monitorHeight * (zone.TopPercentage / 100d), MidpointRounding.AwayFromZero);

        x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

        return new Rectangle(x, y, width, height);
    }

    private void ApplyBoundsToInputs(Rectangle bounds)
    {
        if (chkJanelaTelaCheia.Checked)
        {
            return;
        }

        if (nudJanelaX is not null)
        {
            nudJanelaX.Value = AjustarRange(nudJanelaX, bounds.X);
        }

        if (nudJanelaY is not null)
        {
            nudJanelaY.Value = AjustarRange(nudJanelaY, bounds.Y);
        }

        if (nudJanelaLargura is not null)
        {
            nudJanelaLargura.Value = AjustarRange(nudJanelaLargura, bounds.Width);
        }

        if (nudJanelaAltura is not null)
        {
            nudJanelaAltura.Value = AjustarRange(nudJanelaAltura, bounds.Height);
        }
    }

    private static int TryGetRefreshRate(string? deviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return 0;
        }

        try
        {
            var mode = new DEVMODE
            {
                dmSize = (short)Marshal.SizeOf<DEVMODE>(),
            };

            return EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode)
                ? mode.dmDisplayFrequency
                : 0;
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
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
        var button = btnTestarJanela;

        if (button is not null)
        {
            button.Enabled = false;
        }

        try
        {
            await LaunchDummyWindowAsync(monitor, window).ConfigureAwait(true);
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

    private async void btnTestReal_Click(object? sender, EventArgs e)
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

        if (!TryBuildCurrentApp("Teste de aplicativo", out var app, out var monitor, out var bounds))
        {
            return;
        }

        var button = btnTestReal;
        if (button is not null)
        {
            button.Enabled = false;
        }

        try
        {
            await _appRunner.RunAndPositionAsync(app, monitor, bounds).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível executar o aplicativo real: {ex.Message}",
                "Teste de aplicativo",
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

    private bool TryBuildCurrentApp(
        string messageTitle,
        out ProgramaConfig app,
        out MonitorInfo monitor,
        out Rectangle bounds,
        string? executableOverride = null,
        string? argumentsOverride = null,
        bool requireExecutable = true)
    {
        app = default!;
        monitor = default!;
        bounds = default;

        var selectedMonitor = GetSelectedMonitor();
        if (selectedMonitor is null)
        {
            MessageBox.Show(
                this,
                "Selecione um monitor para testar a posição.",
                messageTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        var config = ConstruirPrograma();

        if (executableOverride is not null)
        {
            var overridePath = executableOverride.Trim();
            config = config with { ExecutablePath = overridePath };
        }

        if (argumentsOverride is not null)
        {
            var overrideArguments = string.IsNullOrWhiteSpace(argumentsOverride)
                ? null
                : argumentsOverride;
            config = config with { Arguments = overrideArguments };
        }

        var executablePath = config.ExecutablePath;
        if (requireExecutable && string.IsNullOrWhiteSpace(executablePath))
        {
            MessageBox.Show(
                this,
                "Informe um executável válido antes de executar o teste.",
                messageTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        if (requireExecutable && !string.IsNullOrWhiteSpace(executablePath) && !File.Exists(executablePath))
        {
            MessageBox.Show(
                this,
                "Executável não encontrado. Ajuste o caminho antes de executar o teste.",
                messageTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        var resolvedBounds = WindowPlacementHelper.ResolveBounds(config.Window, selectedMonitor);

        app = config;
        monitor = selectedMonitor;
        bounds = resolvedBounds;
        return true;
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
            var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);
            WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
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
            errorProvider.SetError(txtId, "Informe um identificador." );
            valido = false;
        }
        else
        {
            errorProvider.SetError(txtId, string.Empty);
        }

        if (string.IsNullOrWhiteSpace(txtExecutavel.Text))
        {
            errorProvider.SetError(txtExecutavel, "Informe o executável." );
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
        monitorPreviewDisplay?.Unbind();
    }

    private void AppsTab_ExecutableChosen(object? sender, AppSelectionEventArgs e)
    {
        txtExecutavel.Text = e.ExecutablePath;
        appsTabControl!.ExecutablePath = e.ExecutablePath;
        UpdateExePreview();
    }

    private void AppsTab_ExecutableCleared(object? sender, EventArgs e)
    {
        txtExecutavel.Text = string.Empty;
        appsTabControl!.ExecutablePath = string.Empty;
        UpdateExePreview();
    }

    private void AppsTab_ArgumentsChanged(object? sender, string e)
    {
        txtArgumentos.Text = e;
        UpdateExePreview();
    }

    private void UpdateExePreview()
    {
        if (txtCmdPreviewExe is null)
        {
            return;
        }

        var path = txtExecutavel?.Text?.Trim() ?? string.Empty;
        var args = txtArgumentos?.Text?.Trim() ?? string.Empty;

        string preview;
        if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(args))
        {
            preview = string.Empty;
        }
        else if (string.IsNullOrWhiteSpace(path))
        {
            preview = args;
        }
        else if (string.IsNullOrWhiteSpace(args))
        {
            preview = $"\"{path}\"";
        }
        else
        {
            preview = $"\"{path}\" {args}";
        }

        txtCmdPreviewExe.Text = preview;
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

        if (!TryBuildCurrentApp(
                "Teste de aplicativo",
                out var app,
                out var monitor,
                out var bounds,
                e.ExecutablePath,
                e.Arguments,
                requireExecutable: false))
        {
            return;
        }

        var executablePath = app.ExecutablePath;
        var hasExecutable = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath);

        try
        {
            if (hasExecutable)
            {
                await _appRunner.RunAndPositionAsync(app, monitor, bounds).ConfigureAwait(true);
            }
            else
            {
                await LaunchDummyWindowAsync(monitor, app.Window).ConfigureAwait(true);
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        private const int CchDeviceName = 32;
        private const int CchFormName = 32;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchFormName)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private sealed class MonitorOption
    {
        public MonitorOption(string? monitorId, MonitorInfo? monitor, string displayName)
        {
            MonitorId = monitorId;
            Monitor = monitor;
            DisplayName = displayName;
            Tag = monitor;
        }

        public string? MonitorId { get; }

        public MonitorInfo? Monitor { get; }

        public MonitorInfo? Tag { get; }

        public string DisplayName { get; }

        public static MonitorOption Empty()
            => new(null, null, "Nenhum monitor disponível");

        public override string ToString() => DisplayName;
    }
}
