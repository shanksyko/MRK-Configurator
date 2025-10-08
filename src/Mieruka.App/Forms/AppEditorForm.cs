#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Services;
using Mieruka.App.Simulation;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using Mieruka.Core.Services;
using Mieruka.Core.Infra;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;
using Serilog;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : Form
{
    private static readonly ILogger Logger = Log.ForContext<AppEditorForm>();
    private static readonly TimeSpan WindowTestTimeout = TimeSpan.FromSeconds(5);
    private const int EnumCurrentSettings = -1;
    private static readonly TimeSpan PreviewResumeDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan HoverThrottleInterval = TimeSpan.FromMilliseconds(1000d / 30d);
    private static readonly MethodInfo? ClearAppsInventorySelectionMethod =
        typeof(AppsTab).GetMethod("ClearSelection", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Color[] SimulationPalette =
    {
        Color.FromArgb(0x4A, 0x90, 0xE2),
        Color.FromArgb(0x50, 0xC8, 0x8D),
        Color.FromArgb(0xF5, 0xA6, 0x2B),
        Color.FromArgb(0xD4, 0x6A, 0x6A),
        Color.FromArgb(0x9B, 0x59, 0xB6),
        Color.FromArgb(0x1A, 0xBC, 0x9C),
        Color.FromArgb(0xE6, 0x7E, 0x22),
        Color.FromArgb(0x2E, 0x86, 0xAB),
    };

    private enum ExecSourceMode
    {
        None,
        Inventory,
        Custom,
    }

    private readonly BindingList<SiteConfig> _sites;
    private readonly ProgramaConfig? _original;
    private readonly IReadOnlyList<MonitorInfo>? _providedMonitors;
    private readonly List<MonitorInfo> _monitors;
    private readonly string? _preferredMonitorId;
    private readonly IAppRunner _appRunner;
    private readonly IList<ProgramaConfig>? _profileApps;
    private readonly BindingList<ProfileItemMetadata> _profileItems = new();
    private ProfileItemMetadata? _editingMetadata;
    private bool _suppressCycleUpdates;
    private bool _suppressCycleSelectionEvents;
    private ExecSourceMode _execSourceMode;
    private bool _suppressListSelectionChanged;
    private MonitorInfo? _selectedMonitorInfo;
    private string? _selectedMonitorId;
    private bool _suppressMonitorComboEvents;
    private readonly AppCycleSimulator _cycleSimulator = new();
    private readonly List<SimRectDisplay> _cycleDisplays = new();
    private CancellationTokenSource? _cycleSimulationCts;
    private SimRectDisplay? _activeCycleDisplay;
    private int _nextSimRectIndex;
    private readonly Stopwatch _hoverSw = new();
    private Point? _hoverPendingPoint;
    private Point? _hoverAppliedPoint;
    private CancellationTokenSource? _hoverThrottleCts;
    private readonly IInstalledAppsProvider _installedAppsProvider = new RegistryInstalledAppsProvider();
    private readonly List<InstalledAppInfo> _allApps = new();
    private readonly Label _installedAppsStatusLabel = new();
    private TextBox? _installedAppsSearchBox;
    private bool _installedAppsLoaded;

    public AppEditorForm(
        ProgramaConfig? programa = null,
        IReadOnlyList<MonitorInfo>? monitors = null,
        string? selectedMonitorId = null,
        IAppRunner? appRunner = null,
        IList<ProgramaConfig>? profileApps = null)
    {
        InitializeComponent();

        _ = tabEditor ?? throw new InvalidOperationException("O TabControl do editor não foi carregado.");
        var salvar = btnSalvar ?? throw new InvalidOperationException("O botão Salvar não foi carregado.");
        _ = btnCancelar ?? throw new InvalidOperationException("O botão Cancelar não foi carregado.");
        var sitesControl = sitesEditorControl ?? throw new InvalidOperationException("O controle de sites não foi carregado.");
        var appsTab = appsTabControl ?? throw new InvalidOperationException("A aba de aplicativos não foi carregada.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi configurado.");
        var cycleSource = bsCycle ?? throw new InvalidOperationException("A fonte de dados do ciclo não foi configurada.");
        var cycleGrid = dgvCycle ?? throw new InvalidOperationException("O grid de ciclo não foi carregado.");
        _ = tlpCycle ?? throw new InvalidOperationException("O layout do ciclo não foi carregado.");
        _ = btnCycleUp ?? throw new InvalidOperationException("O botão de mover para cima do ciclo não foi carregado.");
        _ = btnCycleDown ?? throw new InvalidOperationException("O botão de mover para baixo do ciclo não foi carregado.");

        _ = rbExe ?? throw new InvalidOperationException("O seletor de executável não foi carregado.");
        _ = rbBrowser ?? throw new InvalidOperationException("O seletor de navegador não foi carregado.");
        _ = grpInstalledApps ?? throw new InvalidOperationException("O grupo de aplicativos instalados não foi carregado.");
        var installedAppsList = lvApps ?? throw new InvalidOperationException("A lista de aplicativos instalados não foi carregada.");
        _ = btnBrowseExe ?? throw new InvalidOperationException("O botão de procurar executáveis não foi carregado.");
        _ = cmbBrowserEngine ?? throw new InvalidOperationException("O seletor de motor de navegador não foi carregado.");
        _ = lblBrowserDetected ?? throw new InvalidOperationException("O rótulo de navegadores detectados não foi carregado.");
        _ = pnlBrowserOptions ?? throw new InvalidOperationException("O painel de opções de navegador não foi carregado.");

        _ = tlpMonitorPreview ?? throw new InvalidOperationException("O painel de pré-visualização não foi configurado.");
        var previewControl = monitorPreviewDisplay ?? throw new InvalidOperationException("O controle de pré-visualização do monitor não foi configurado.");
        _ = lblMonitorCoordinates ?? throw new InvalidOperationException("O rótulo de coordenadas do monitor não foi configurado.");
        var janelaTab = tpJanela ?? throw new InvalidOperationException("A aba de janela não foi configurada.");

        AcceptButton = salvar;
        CancelButton = btnCancelar;

        _appRunner = appRunner ?? new AppRunner();
        _appRunner.BeforeMoveWindow += AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow += AppRunnerOnAfterMoveWindow;
        _providedMonitors = monitors;
        _monitors = new List<MonitorInfo>();
        _preferredMonitorId = selectedMonitorId;
        _profileApps = profileApps;

        cycleSource.DataSource = _profileItems;
        cycleGrid.AutoGenerateColumns = false;
        cycleGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _profileItems.ListChanged += (_, _) =>
        {
            if (_suppressCycleUpdates)
            {
                return;
            }

            UpdateCycleButtons();
        };

        RefreshMonitorSnapshot();

        _sites = new BindingList<SiteConfig>();
        sitesControl.Sites = _sites;
        sitesControl.AddRequested += SitesEditorControl_AddRequested;
        sitesControl.RemoveRequested += SitesEditorControl_RemoveRequested;
        sitesControl.CloneRequested += SitesEditorControl_CloneRequested;

        ConfigureInstalledAppsSection(installedAppsList);

        appsTab.ExecutableChosen += AppsTab_ExecutableChosen;
        appsTab.ExecutableCleared += AppsTab_ExecutableCleared;
        appsTab.ArgumentsChanged += AppsTab_ArgumentsChanged;
        appsTab.OpenRequested += AppsTab_OpenRequestedAsync;
        appsTab.TestRequested += AppsTab_TestRequestedAsync;

        txtExecutavel.TextChanged += (_, _) => UpdateExePreview();
        txtArgumentos.TextChanged += (_, _) => UpdateExePreview();
        txtId.TextChanged += txtId_TextChanged;

        cboMonitores.SelectedIndexChanged += cboMonitores_SelectedIndexChanged;
        PopulateMonitorCombo(programa);

        previewControl.MouseMovedInMonitorSpace += MonitorPreviewDisplay_MouseMovedInMonitorSpace;
        previewControl.MonitorMouseLeft += MonitorPreviewDisplay_MonitorMouseLeft;

        janelaTab.SizeChanged += (_, _) => AdjustMonitorPreviewWidth();

        chkJanelaTelaCheia.CheckedChanged += chkJanelaTelaCheia_CheckedChanged;
        chkAutoStart.CheckedChanged += (_, _) => InvalidateWindowPreviewOverlay();

        if (nudJanelaX is not null)
        {
            nudJanelaX.ValueChanged += (_, _) => InvalidateWindowPreviewOverlay();
        }

        if (nudJanelaY is not null)
        {
            nudJanelaY.ValueChanged += (_, _) => InvalidateWindowPreviewOverlay();
        }

        if (nudJanelaLargura is not null)
        {
            nudJanelaLargura.ValueChanged += (_, _) => InvalidateWindowPreviewOverlay();
        }

        if (nudJanelaAltura is not null)
        {
            nudJanelaAltura.ValueChanged += (_, _) => InvalidateWindowPreviewOverlay();
        }

        AdjustMonitorPreviewWidth();
        UpdateWindowInputsState();
        UpdateMonitorCoordinateLabel(null);

        Disposed += AppEditorForm_Disposed;
        Shown += async (_, __) => await SafeLoadAppsAsync().ConfigureAwait(true);

        InitializeCycleMetadata(profileApps, programa);

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

        InitializeCycleSimulation();
        ApplyAppTypeUI();
    }

    private void ConfigureInstalledAppsSection(ListView installedAppsList)
    {
        var statusLabel = _installedAppsStatusLabel;
        statusLabel.Dock = DockStyle.Bottom;
        statusLabel.Height = 20;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.ForeColor = SystemColors.GrayText;
        statusLabel.Padding = new Padding(0, 4, 0, 0);
        statusLabel.Visible = false;
        grpInstalledApps.Controls.Add(statusLabel);

        _installedAppsSearchBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Buscar aplicativos instalados...",
        };
        _installedAppsSearchBox.TextChanged += InstalledAppsSearch_TextChanged;
        grpInstalledApps.Controls.Add(_installedAppsSearchBox);
        grpInstalledApps.Controls.SetChildIndex(_installedAppsSearchBox, 0);
        grpInstalledApps.Controls.SetChildIndex(installedAppsList, 1);

        EnsureInstalledAppsColumns(installedAppsList);
    }

    public ProgramaConfig? Resultado { get; private set; }

    public BindingList<SiteConfig> ResultadoSites => new(_sites.Select(site => site with { }).ToList());

    public string? SelectedMonitorId => _selectedMonitorId;

    private IEnumerable<ProgramaConfig> CurrentProfileItems()
    {
        return _profileApps ?? Array.Empty<ProgramaConfig>();
    }

    public void SetProfileApplications(IEnumerable<ProgramaConfig>? apps)
    {
        List<ProgramaConfig>? replacements = null;

        if (apps is not null)
        {
            replacements = new List<ProgramaConfig>();

            foreach (var app in apps)
            {
                if (app is null)
                {
                    continue;
                }

                replacements.Add(app);
            }
        }

        if (_profileApps is not null)
        {
            _profileApps.Clear();

            if (replacements is not null)
            {
                foreach (var app in replacements)
                {
                    _profileApps.Add(app);
                }
            }
        }

        RebuildSimulationOverlays();
    }

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

    private void InitializeCycleSimulation()
    {
        if (flowCycleItems is null)
        {
            return;
        }

        _sites.ListChanged += Sites_ListChanged;

        if (txtId is not null)
        {
            txtId.TextChanged += (_, _) => RebuildSimRects();
        }

        if (cycleToolTip is not null)
        {
            if (btnCyclePlay is not null)
            {
                cycleToolTip.SetToolTip(btnCyclePlay, "Executa a simulação do ciclo.");
            }

            if (btnCycleStep is not null)
            {
                cycleToolTip.SetToolTip(btnCycleStep, "Avança manualmente para o próximo item do ciclo.");
            }

            if (btnCycleStop is not null)
            {
                cycleToolTip.SetToolTip(btnCycleStop, "Interrompe a simulação atual.");
            }

            if (chkCycleRedeDisponivel is not null)
            {
                cycleToolTip.SetToolTip(chkCycleRedeDisponivel, "Simula a disponibilidade de rede para itens dependentes.");
            }
        }

        RebuildSimRects();
    }

    private void ApplyAppTypeUI()
    {
        var isExecutable = rbExe?.Checked ?? false;
        var isBrowser = rbBrowser?.Checked ?? false;

        if (grpInstalledApps is not null)
        {
            grpInstalledApps.Visible = isExecutable;
            grpInstalledApps.Enabled = isExecutable;
            grpInstalledApps.Refresh();
        }

        if (lvApps is not null)
        {
            lvApps.Visible = isExecutable;
            lvApps.Enabled = isExecutable;
        }

        if (btnBrowseExe is not null)
        {
            btnBrowseExe.Visible = isExecutable;
            btnBrowseExe.Enabled = isExecutable;
        }

        if (txtExecutavel is not null)
        {
            txtExecutavel.Enabled = isExecutable;
            txtExecutavel.ReadOnly = !isExecutable;
            txtExecutavel.TabStop = isExecutable;
        }

        if (txtArgumentos is not null)
        {
            txtArgumentos.Enabled = isExecutable;
            txtArgumentos.ReadOnly = !isExecutable;
            txtArgumentos.TabStop = isExecutable;
        }

        if (isBrowser)
        {
            BindDetectedBrowsers();
        }
        else
        {
            if (cmbBrowserEngine is not null)
            {
                cmbBrowserEngine.Enabled = false;
            }

            if (pnlBrowserOptions is not null)
            {
                pnlBrowserOptions.Enabled = false;
            }

            if (sitesEditorControl is not null)
            {
                sitesEditorControl.Enabled = false;
            }
        }

        if (cmbBrowserEngine is not null)
        {
            cmbBrowserEngine.Visible = isBrowser;
            cmbBrowserEngine.TabStop = isBrowser && cmbBrowserEngine.Enabled;
        }

        if (lblBrowserDetected is not null)
        {
            lblBrowserDetected.Visible = isBrowser && !string.IsNullOrWhiteSpace(lblBrowserDetected.Text);
        }

        if (pnlBrowserOptions is not null)
        {
            pnlBrowserOptions.Visible = isBrowser;
        }

        if (sitesEditorControl is not null)
        {
            sitesEditorControl.Visible = isBrowser;
        }
    }

    private async Task SafeLoadAppsAsync()
    {
        if (_installedAppsLoaded)
        {
            return;
        }

        _installedAppsLoaded = true;

        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        UseWaitCursor = true;
        var previousCursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;

        try
        {
            var apps = await _installedAppsProvider.QueryAsync().ConfigureAwait(true);
            _allApps.Clear();
            _allApps.AddRange(apps);

            PopulateInstalledApps(_allApps);
            appsTabControl?.SetInstalledApps(apps);
            UpdateInstalledAppsStatus(string.Empty, isError: false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao carregar a lista de aplicativos instalados.");
            _allApps.Clear();
            PopulateInstalledApps(Array.Empty<InstalledAppInfo>());
            UpdateInstalledAppsStatus("Não foi possível carregar a lista de aplicativos instalados.", isError: true);
        }
        finally
        {
            Cursor.Current = previousCursor;
            UseWaitCursor = false;
            ApplyAppTypeUI();
        }
    }

    private void BindDetectedBrowsers()
    {
        if (cmbBrowserEngine is null || lblBrowserDetected is null || pnlBrowserOptions is null)
        {
            return;
        }

        var detections = BrowserRegistry.Detect();

        var supported = detections
            .Where(browser => browser.IsSupported)
            .ToList();

        cmbBrowserEngine.BeginUpdate();
        try
        {
            cmbBrowserEngine.Items.Clear();
            foreach (var browser in supported)
            {
                cmbBrowserEngine.Items.Add(new BrowserComboItem(browser));
            }
        }
        finally
        {
            cmbBrowserEngine.EndUpdate();
        }

        var items = cmbBrowserEngine.Items.Cast<object>()
            .OfType<BrowserComboItem>()
            .ToList();

        BrowserComboItem? selected = items
            .FirstOrDefault(item => item.Installation.Engine == BrowserType.Chrome && item.Installation.IsDetected)
            ?? items.FirstOrDefault(item => item.Installation.IsDetected)
            ?? items.FirstOrDefault(item => item.Installation.Engine == BrowserType.Chrome)
            ?? items.FirstOrDefault();

        if (selected is not null)
        {
            cmbBrowserEngine.SelectedItem = selected;
        }
        else
        {
            cmbBrowserEngine.SelectedIndex = -1;
        }

        lblBrowserDetected.Text = BuildBrowserDetectionMessage(detections);

        var hasSupported = items.Count > 0;
        cmbBrowserEngine.Enabled = hasSupported;
        pnlBrowserOptions.Enabled = hasSupported;

        if (sitesEditorControl is not null)
        {
            sitesEditorControl.Enabled = hasSupported;
        }
    }

    private static string BuildBrowserDetectionMessage(IReadOnlyList<BrowserRegistry.BrowserInstallation> detections)
    {
        if (detections.Count == 0)
        {
            return "Nenhum navegador foi detectado.";
        }

        var supportedDetected = detections
            .Where(detection => detection.IsSupported && detection.IsDetected)
            .Select(detection => detection.DisplayName)
            .ToList();

        var supportedMissing = detections
            .Where(detection => detection.IsSupported && !detection.IsDetected)
            .Select(detection => detection.DisplayName)
            .ToList();

        var unsupportedDetected = detections
            .Where(detection => !detection.IsSupported && detection.IsDetected)
            .Select(detection => detection.DisplayName)
            .ToList();

        var builder = new StringBuilder();

        if (supportedDetected.Count > 0)
        {
            builder.Append("Navegadores suportados detectados: ");
            builder.Append(string.Join(", ", supportedDetected));
            builder.Append('.');
        }
        else
        {
            builder.Append("Nenhum navegador suportado foi encontrado.");
        }

        if (supportedMissing.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append("Não encontrados: ");
            builder.Append(string.Join(", ", supportedMissing));
            builder.Append('.');
        }

        if (unsupportedDetected.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append("Detectados (não suportados): ");
            builder.Append(string.Join(", ", unsupportedDetected));
            builder.Append('.');
        }

        return builder.ToString();
    }

    private sealed class BrowserComboItem
    {
        internal BrowserComboItem(BrowserRegistry.BrowserInstallation installation)
        {
            Installation = installation;
        }

        internal BrowserRegistry.BrowserInstallation Installation { get; }

        internal BrowserType Engine => Installation.Engine ?? BrowserType.Chrome;

        public override string ToString()
        {
            return Installation.IsDetected
                ? Installation.DisplayName
                : $"{Installation.DisplayName} (não encontrado)";
        }
    }

    private void InstalledAppsSearch_TextChanged(object? sender, EventArgs e)
    {
        if (lvApps is null)
        {
            return;
        }

        var term = _installedAppsSearchBox?.Text;
        if (string.IsNullOrWhiteSpace(term))
        {
            PopulateInstalledApps(_allApps);
            return;
        }

        var filtered = _allApps
            .Where(app =>
                app.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(app.Vendor) && app.Vendor.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                app.ExecutablePath.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(app.Source) && app.Source.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PopulateInstalledApps(filtered);
    }

    private void PopulateInstalledApps(IReadOnlyList<InstalledAppInfo> apps)
    {
        if (lvApps is null)
        {
            return;
        }

        EnsureInstalledAppsColumns(lvApps);

        lvApps.BeginUpdate();
        try
        {
            lvApps.Items.Clear();
            foreach (var app in apps)
            {
                lvApps.Items.Add(CreateListViewItem(app));
            }

            lvApps.SelectedItems.Clear();
        }
        finally
        {
            lvApps.EndUpdate();
        }

        if (lvApps.Columns.Count > 0)
        {
            lvApps.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
        }
    }

    private static ListViewItem CreateListViewItem(InstalledAppInfo app)
    {
        var item = new ListViewItem(app.Name)
        {
            Tag = app,
        };

        item.SubItems.Add(app.Version ?? string.Empty);
        item.SubItems.Add(app.Vendor ?? string.Empty);
        item.SubItems.Add(app.ExecutablePath);
        item.SubItems.Add(app.Source);

        return item;
    }

    private void UpdateInstalledAppsStatus(string? message, bool isError)
    {
        if (_installedAppsStatusLabel is null)
        {
            return;
        }

        var text = message ?? string.Empty;
        _installedAppsStatusLabel.Text = text;
        _installedAppsStatusLabel.ForeColor = isError ? Color.Maroon : SystemColors.GrayText;
        _installedAppsStatusLabel.Visible = !string.IsNullOrWhiteSpace(text);
    }

    private static void EnsureInstalledAppsColumns(ListView listView)
    {
        if (listView.Columns.Count > 0)
        {
            return;
        }

        listView.Columns.Add("Nome", 200, HorizontalAlignment.Left);
        listView.Columns.Add("Versão", 80, HorizontalAlignment.Left);
        listView.Columns.Add("Fornecedor", 120, HorizontalAlignment.Left);
        listView.Columns.Add("Caminho", 320, HorizontalAlignment.Left);
        listView.Columns.Add("Origem", 80, HorizontalAlignment.Left);
    }

    private void Sites_ListChanged(object? sender, ListChangedEventArgs e)
    {
        RebuildSimRects();
    }

    private void RebuildSimRects()
    {
        if (IsDisposed)
        {
            return;
        }

        if (_cycleSimulationCts is not null)
        {
            StopCycleSimulation();

            if (IsHandleCreated)
            {
                BeginInvoke(new Action(RebuildSimRects));
            }

            return;
        }

        _nextSimRectIndex = 0;
        ClearActiveSimRect();

        if (flowCycleItems is null)
        {
            return;
        }

        flowCycleItems.SuspendLayout();
        try
        {
            flowCycleItems.Controls.Clear();
        }
        finally
        {
            flowCycleItems.ResumeLayout(false);
        }

        DisposeSimRectDisplays();

        var items = BuildSimRectList();
        if (items.Count == 0)
        {
            var placeholder = new Label
            {
                AutoSize = true,
                Margin = new Padding(12),
                Text = "Nenhum item disponível para simulação.",
            };

            flowCycleItems.Controls.Add(placeholder);
            UpdateCycleControlsState();
            return;
        }

        flowCycleItems.SuspendLayout();
        try
        {
            foreach (var rect in items)
            {
                var display = CreateSimRectDisplay(rect);
                _cycleDisplays.Add(display);
                flowCycleItems.Controls.Add(display.Panel);
                ApplySimRectTooltip(display);
            }
        }
        finally
        {
            flowCycleItems.ResumeLayout(false);
        }

        UpdateCycleControlsState();
    }

    private IReadOnlyList<AppCycleSimulator.SimRect> BuildSimRectList()
    {
        var items = new List<AppCycleSimulator.SimRect>();

        var appId = txtId?.Text?.Trim();
        var appLabel = string.IsNullOrWhiteSpace(appId) ? "Aplicativo" : appId;
        var appDetails = txtExecutavel is null
            ? null
            : (string.IsNullOrWhiteSpace(txtExecutavel.Text) ? null : txtExecutavel.Text.Trim());

        items.Add(new AppCycleSimulator.SimRect(
            "app",
            $"Aplicativo: {appLabel}",
            RequiresNetwork: false,
            DelayMs: 400,
            Details: appDetails));

        var index = 0;
        foreach (var site in _sites)
        {
            index++;
            var siteId = string.IsNullOrWhiteSpace(site.Id) ? $"Site {index}" : site.Id.Trim();
            var details = string.IsNullOrWhiteSpace(site.Url) ? null : site.Url.Trim();

            items.Add(new AppCycleSimulator.SimRect(
                $"site:{index}",
                $"Site: {siteId}",
                RequiresNetwork: true,
                DelayMs: null,
                Details: details));
        }

        return items;
    }

    private SimRectDisplay CreateSimRectDisplay(AppCycleSimulator.SimRect rect)
    {
        var panel = new Panel
        {
            Width = 200,
            Height = 120,
            Margin = new Padding(12),
            Padding = new Padding(4),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.ControlLightLight,
        };

        var label = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = rect.DisplayName,
            AutoEllipsis = true,
            Padding = new Padding(4),
        };

        panel.Controls.Add(label);
        panel.Tag = rect;

        return new SimRectDisplay(rect, panel, label);
    }

    private void ApplySimRectTooltip(SimRectDisplay display)
    {
        if (cycleToolTip is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append(display.Rect.DisplayName);
        builder.AppendLine();
        builder.Append("Rede necessária: ");
        builder.Append(display.Rect.RequiresNetwork ? "Sim" : "Não");
        builder.AppendLine();
        builder.Append("Duração simulada: ");
        var delay = display.Rect.DelayMs ?? AppCycleSimulator.DefaultDelayMs;
        builder.Append(delay.ToString(CultureInfo.InvariantCulture));
        builder.Append(" ms");

        if (!string.IsNullOrWhiteSpace(display.Rect.Details))
        {
            builder.AppendLine();
            builder.Append(display.Rect.Details);
        }

        if (display.LastResult == SimRectStatus.Completed && display.LastActivation is DateTime completed)
        {
            builder.AppendLine();
            builder.Append("Última execução: ");
            builder.Append(completed.ToString("T", CultureInfo.CurrentCulture));
        }
        else if (display.LastResult == SimRectStatus.Skipped && display.LastSkipped is DateTime skipped)
        {
            builder.AppendLine();
            builder.Append("Ignorado às ");
            builder.Append(skipped.ToString("T", CultureInfo.CurrentCulture));
            builder.Append(" (rede indisponível)");
        }

        var tooltip = builder.ToString();
        cycleToolTip.SetToolTip(display.Panel, tooltip);
        cycleToolTip.SetToolTip(display.Label, tooltip);
    }

    private void RefreshSimRectTooltips()
    {
        foreach (var display in _cycleDisplays)
        {
            ApplySimRectTooltip(display);
        }
    }

    private async Task RunContinuousSimulationAsync()
    {
        if (_cycleSimulationCts is not null || _cycleDisplays.Count == 0)
        {
            return;
        }

        var tokenSource = new CancellationTokenSource();
        _cycleSimulationCts = tokenSource;
        UpdateCycleControlsState();

        try
        {
            var token = tokenSource.Token;

            while (!token.IsCancellationRequested)
            {
                if (_cycleDisplays.Count == 0)
                {
                    break;
                }

                var display = _cycleDisplays[_nextSimRectIndex];
                _nextSimRectIndex = (_nextSimRectIndex + 1) % _cycleDisplays.Count;

                if (display.Rect.RequiresNetwork && !EvaluateNetworkAvailability())
                {
                    display.LastResult = SimRectStatus.Skipped;
                    display.LastActivation = null;
                    display.LastSkipped = DateTime.Now;
                    ApplySimRectTooltip(display);

                    if (_cycleDisplays.All(d => d.Rect.RequiresNetwork))
                    {
                        await Task.Delay(AppCycleSimulator.DefaultDelayMs, token);
                    }

                    continue;
                }

                display.LastResult = SimRectStatus.None;
                display.LastActivation = null;
                display.LastSkipped = null;
                ApplySimRectTooltip(display);

                await _cycleSimulator.SimulateAsync(
                    new[] { display.Rect },
                    EvaluateNetworkAvailability,
                    OnSimRectStarted,
                    OnSimRectEnded,
                    token);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignorar cancelamentos solicitados pelo usuário.
        }
        finally
        {
            _cycleSimulationCts = null;
            tokenSource.Dispose();
            UpdateCycleControlsState();
            ClearActiveSimRect();
        }
    }

    private async Task RunSingleStepAsync()
    {
        if (_cycleSimulationCts is not null || _cycleDisplays.Count == 0)
        {
            return;
        }

        if (btnCycleStep is not null)
        {
            btnCycleStep.Enabled = false;
        }

        try
        {
            var attempts = 0;
            while (attempts < _cycleDisplays.Count)
            {
                var display = _cycleDisplays[_nextSimRectIndex];
                _nextSimRectIndex = (_nextSimRectIndex + 1) % _cycleDisplays.Count;
                attempts++;

                if (display.Rect.RequiresNetwork && !EvaluateNetworkAvailability())
                {
                    display.LastResult = SimRectStatus.Skipped;
                    display.LastActivation = null;
                    display.LastSkipped = DateTime.Now;
                    ApplySimRectTooltip(display);
                    continue;
                }

                display.LastResult = SimRectStatus.None;
                display.LastActivation = null;
                display.LastSkipped = null;
                ApplySimRectTooltip(display);

                await _cycleSimulator.SimulateAsync(
                    new[] { display.Rect },
                    EvaluateNetworkAvailability,
                    OnSimRectStarted,
                    OnSimRectEnded);

                break;
            }
        }
        finally
        {
            UpdateCycleControlsState();
        }
    }

    private void StopCycleSimulation()
    {
        var cts = _cycleSimulationCts;
        if (cts is null)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            cts.Cancel();
        }

        UpdateCycleControlsState();
    }

    private void UpdateCycleControlsState()
    {
        var hasItems = _cycleDisplays.Count > 0;
        var isRunning = _cycleSimulationCts is not null;

        if (btnCyclePlay is not null)
        {
            btnCyclePlay.Enabled = hasItems && !isRunning;
        }

        if (btnCycleStep is not null)
        {
            btnCycleStep.Enabled = hasItems && !isRunning;
        }

        if (btnCycleStop is not null)
        {
            btnCycleStop.Enabled = isRunning;
        }
    }

    private void OnSimRectStarted(AppCycleSimulator.SimRect rect)
    {
        var display = FindDisplay(rect);
        if (display is null)
        {
            return;
        }

        if (_activeCycleDisplay is not null && !ReferenceEquals(_activeCycleDisplay, display))
        {
            SetSimRectActive(_activeCycleDisplay, isActive: false);
        }

        SetSimRectActive(display, isActive: true);
        _activeCycleDisplay = display;
    }

    private void OnSimRectEnded(AppCycleSimulator.SimRect rect)
    {
        var display = FindDisplay(rect);
        if (display is null)
        {
            return;
        }

        display.LastResult = SimRectStatus.Completed;
        display.LastActivation = DateTime.Now;
        display.LastSkipped = null;
        ApplySimRectTooltip(display);

        if (_activeCycleDisplay is not null && ReferenceEquals(_activeCycleDisplay, display))
        {
            SetSimRectActive(display, isActive: false);
            _activeCycleDisplay = null;
        }
    }

    private SimRectDisplay? FindDisplay(AppCycleSimulator.SimRect rect)
    {
        return _cycleDisplays.FirstOrDefault(display => ReferenceEquals(display.Rect, rect));
    }

    private void SetSimRectActive(SimRectDisplay display, bool isActive)
    {
        if (display.Panel.IsDisposed)
        {
            return;
        }

        if (isActive)
        {
            display.Panel.BorderStyle = BorderStyle.Fixed3D;
            display.Panel.BackColor = Color.FromArgb(32, 146, 204);
            display.Label.ForeColor = Color.White;
            display.Label.Font = display.BoldFont;
        }
        else
        {
            display.Panel.BorderStyle = BorderStyle.FixedSingle;
            display.Panel.BackColor = SystemColors.ControlLightLight;
            display.Label.ForeColor = SystemColors.ControlText;
            display.Label.Font = display.NormalFont;
        }
    }

    private void ClearActiveSimRect()
    {
        if (_activeCycleDisplay is null)
        {
            return;
        }

        SetSimRectActive(_activeCycleDisplay, isActive: false);
        _activeCycleDisplay = null;
    }

    private bool EvaluateNetworkAvailability()
    {
        return chkCycleRedeDisponivel?.Checked ?? true;
    }

    private void DisposeSimRectDisplays()
    {
        ClearActiveSimRect();

        foreach (var display in _cycleDisplays)
        {
            try
            {
                if (!display.Panel.IsDisposed)
                {
                    display.Panel.Dispose();
                }
            }
            catch
            {
                // Ignorar falhas durante o descarte dos painéis.
            }

            display.Dispose();
        }

        _cycleDisplays.Clear();
    }

    private async void btnCyclePlay_Click(object? sender, EventArgs e)
    {
        await RunContinuousSimulationAsync();
    }

    private async void btnCycleStep_Click(object? sender, EventArgs e)
    {
        await RunSingleStepAsync();
    }

    private void btnCycleStop_Click(object? sender, EventArgs e)
    {
        StopCycleSimulation();
    }

    private void rbExe_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyAppTypeUI();
    }

    private void rbBrowser_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyAppTypeUI();
    }

    private void chkCycleRedeDisponivel_CheckedChanged(object? sender, EventArgs e)
    {
        RefreshSimRectTooltips();
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
            monitorPreviewDisplay?.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
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
        RebuildSimulationOverlays();
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

        _hoverPendingPoint = point;

        if (!_hoverSw.IsRunning)
        {
            _hoverSw.Start();
        }

        if (_hoverAppliedPoint is null || _hoverSw.Elapsed >= HoverThrottleInterval)
        {
            CancelHoverThrottleTimer();
            ApplyPendingHoverPoint(enforceInterval: false);
            return;
        }

        var remaining = HoverThrottleInterval - _hoverSw.Elapsed;
        ScheduleHoverPointUpdate(remaining);
    }

    private void MonitorPreviewDisplay_MonitorMouseLeft(object? sender, EventArgs e)
    {
        UpdateMonitorCoordinateLabel(null);

        _hoverPendingPoint = null;
        _hoverAppliedPoint = null;
        _hoverSw.Reset();
        CancelHoverThrottleTimer();

        _ = ClampWindowInputsToMonitor(null);
        InvalidateWindowPreviewOverlay();
    }

    private void ScheduleHoverPointUpdate(TimeSpan delay)
    {
        if (IsDisposed)
        {
            return;
        }

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        CancelHoverThrottleTimer();

        var source = new CancellationTokenSource();
        _hoverThrottleCts = source;
        FlushHoverPointAsync(delay, source.Token);
    }

    private async void FlushHoverPointAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token).ConfigureAwait(true);
            }
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (token.IsCancellationRequested || IsDisposed)
        {
            return;
        }

        CancelHoverThrottleTimer();
        ApplyPendingHoverPoint(enforceInterval: true);
    }

    private void ApplyPendingHoverPoint(bool enforceInterval)
    {
        if (_hoverPendingPoint is not Point pending)
        {
            return;
        }

        if (!_hoverSw.IsRunning)
        {
            _hoverSw.Start();
        }

        if (enforceInterval && _hoverSw.Elapsed < HoverThrottleInterval)
        {
            var remaining = HoverThrottleInterval - _hoverSw.Elapsed;
            ScheduleHoverPointUpdate(remaining);
            return;
        }

        if (_hoverAppliedPoint is Point applied && applied == pending)
        {
            _hoverSw.Restart();
            return;
        }

        _hoverSw.Restart();
        _hoverAppliedPoint = pending;
        _ = ClampWindowInputsToMonitor(pending);
    }

    private void CancelHoverThrottleTimer()
    {
        var pending = _hoverThrottleCts;
        if (pending is null)
        {
            return;
        }

        _hoverThrottleCts = null;

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignorar cancelamentos após descarte.
        }
        catch (AggregateException)
        {
            // Ignorar cancelamentos concorrentes.
        }

        pending.Dispose();
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
        RebuildSimulationOverlays();
        monitorPreviewDisplay?.Invalidate();
        monitorPreviewDisplay?.Update();
    }

    private void RebuildSimulationOverlays()
    {
        if (monitorPreviewDisplay is null)
        {
            return;
        }

        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            monitorPreviewDisplay.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
            return;
        }

        var monitorId = MonitorIdentifier.Create(monitor);
        if (string.IsNullOrWhiteSpace(monitorId))
        {
            monitorPreviewDisplay.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
            return;
        }

        var current = ConstruirPrograma();
        var overlayApps = new List<ProgramaConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in CurrentProfileItems())
        {
            if (app is null)
            {
                continue;
            }

            var candidate = app;
            if (!string.IsNullOrWhiteSpace(current.Id) &&
                string.Equals(app.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            {
                candidate = current;
            }

            if (string.IsNullOrWhiteSpace(candidate.Id) || !seen.Add(candidate.Id))
            {
                continue;
            }

            overlayApps.Add(candidate);
        }

        if (!string.IsNullOrWhiteSpace(current.Id))
        {
            if (seen.Add(current.Id))
            {
                overlayApps.Add(current);
            }
        }
        else
        {
            overlayApps.Add(current);
        }

        var overlays = new List<MonitorPreviewDisplay.SimRect>();
        var order = 1;

        foreach (var app in overlayApps)
        {
            var isCurrent = !string.IsNullOrWhiteSpace(current.Id) &&
                string.Equals(app.Id, current.Id, StringComparison.OrdinalIgnoreCase);

            if (!app.AutoStart && !isCurrent)
            {
                continue;
            }

            var resolvedMonitor = ResolveMonitorForApp(app);
            if (resolvedMonitor is null && isCurrent)
            {
                resolvedMonitor = monitor;
            }

            if (resolvedMonitor is null)
            {
                continue;
            }

            var resolvedId = MonitorIdentifier.Create(resolvedMonitor);
            if (string.IsNullOrWhiteSpace(resolvedId) ||
                !string.Equals(resolvedId, monitorId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativeBounds = CalculateMonitorRelativeBounds(app.Window, resolvedMonitor);
            if (relativeBounds.Width <= 0 || relativeBounds.Height <= 0)
            {
                continue;
            }

            var baseColor = ResolveSimulationColor(app.Id);
            var label = string.IsNullOrWhiteSpace(app.Window.Title) ? app.Id : app.Window.Title;

            overlays.Add(new MonitorPreviewDisplay.SimRect
            {
                MonRel = relativeBounds,
                Color = baseColor,
                Order = order,
                Title = label ?? string.Empty,
                RequiresNetwork = app.RequiresNetwork,
                AskBefore = app.AskBeforeLaunch,
            });

            order++;
        }

        monitorPreviewDisplay.SetSimulationRects(overlays);
    }

    private MonitorInfo? ResolveMonitorForApp(ProgramaConfig app)
    {
        if (!string.IsNullOrWhiteSpace(app.TargetMonitorStableId))
        {
            var byStableId = WindowPlacementHelper.GetMonitorByStableId(_monitors, app.TargetMonitorStableId);
            if (byStableId is not null)
            {
                return byStableId;
            }
        }

        var monitorKeyId = MonitorIdentifier.Create(app.Window.Monitor);
        if (!string.IsNullOrWhiteSpace(monitorKeyId))
        {
            return WindowPlacementHelper.ResolveMonitor(null, _monitors, app.Window);
        }

        return null;
    }

    private static Rectangle CalculateMonitorRelativeBounds(WindowConfig window, MonitorInfo monitor)
    {
        var monitorWidth = monitor.Width > 0 ? monitor.Width : monitor.Bounds.Width;
        var monitorHeight = monitor.Height > 0 ? monitor.Height : monitor.Bounds.Height;

        if (monitorWidth <= 0 || monitorHeight <= 0)
        {
            return Rectangle.Empty;
        }

        if (window.FullScreen)
        {
            return new Rectangle(0, 0, monitorWidth, monitorHeight);
        }

        var width = window.Width ?? monitorWidth;
        var height = window.Height ?? monitorHeight;
        width = Math.Clamp(width, 1, monitorWidth);
        height = Math.Clamp(height, 1, monitorHeight);

        var x = window.X ?? 0;
        var y = window.Y ?? 0;
        x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

        var relative = new Rectangle(x, y, width, height);

        var bounds = monitor.Bounds;
        var workArea = monitor.WorkArea;
        if (bounds.Width > 0 && bounds.Height > 0 && workArea.Width > 0 && workArea.Height > 0)
        {
            var absolute = new Rectangle(bounds.Left + relative.X, bounds.Top + relative.Y, relative.Width, relative.Height);
            var clamped = DisplayUtils.ClampToWorkArea(absolute, workArea);
            relative = new Rectangle(
                clamped.Left - bounds.Left,
                clamped.Top - bounds.Top,
                clamped.Width,
                clamped.Height);
        }

        return relative;
    }

    private static Color ResolveSimulationColor(string? id)
    {
        if (SimulationPalette.Length == 0)
        {
            return Color.DodgerBlue;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return SimulationPalette[0];
        }

        const uint basis = 2166136261u;
        const uint prime = 16777619u;
        uint hash = basis;

        foreach (var ch in id)
        {
            hash ^= char.ToUpperInvariant(ch);
            hash *= prime;
        }

        var index = (int)(hash % (uint)SimulationPalette.Length);
        return SimulationPalette[index];
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
            Logger.Error("Falha ao testar a posição em modo simulado.", ex);
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

    partial void OnBeforeMoveWindowUI();

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
            if (!button.Enabled)
            {
                return;
            }

            button.Enabled = false;
        }

        try
        {
            UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;

            SuspendPreviewCapture();
            OnBeforeMoveWindowUI();

            await _appRunner.RunAndPositionAsync(app, monitor, bounds).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Logger.Error("Falha ao executar o aplicativo real durante o teste.", ex);
            MessageBox.Show(
                this,
                $"Não foi possível executar o aplicativo real: {ex.Message}",
                "Teste de aplicativo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Cursor.Current = Cursors.Default;
            SchedulePreviewResume();
            UseWaitCursor = false;

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
            SuspendPreviewCapture();
            try
            {
                WindowMover.MoveTo(handle, bounds, window.AlwaysOnTop, restoreIfMinimized: true);
            }
            finally
            {
                SchedulePreviewResume();
            }
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

    private void AppRunnerOnBeforeMoveWindow(object? sender, EventArgs e)
    {
        SuspendPreviewCapture();
    }

    private void AppRunnerOnAfterMoveWindow(object? sender, EventArgs e)
    {
        SchedulePreviewResume();
    }

    private void SuspendPreviewCapture()
    {
        monitorPreviewDisplay?.SuspendCapture();
    }

    private void SchedulePreviewResume()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(ResumePreviewCaptureWithDelay));
        }
        catch (ObjectDisposedException)
        {
            // Ignorar quando o formulário estiver sendo finalizado.
        }
        catch (InvalidOperationException)
        {
            // Ignorar quando o handle não estiver disponível.
        }
    }

    private async void ResumePreviewCaptureWithDelay()
    {
        try
        {
            await Task.Delay(PreviewResumeDelay).ConfigureAwait(true);
        }
        catch
        {
            // Ignorar interrupções inesperadas.
        }

        if (IsDisposed)
        {
            return;
        }

        ResumePreviewCapture();
    }

    private void ResumePreviewCapture()
    {
        monitorPreviewDisplay?.ResumeCapture();
    }

    private void AppEditorForm_Disposed(object? sender, EventArgs e)
    {
        StopCycleSimulation();
        DisposeSimRectDisplays();
        _appRunner.BeforeMoveWindow -= AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow -= AppRunnerOnAfterMoveWindow;
        CancelHoverThrottleTimer();
    }

    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        if (!ValidarCampos())
        {
            DialogResult = DialogResult.None;
            return;
        }

        Resultado = ConstruirPrograma();
        CommitProfileMetadata();
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

        if (_editingMetadata is not null)
        {
            _editingMetadata.Id = id;
        }

        return (_original ?? new ProgramaConfig()) with
        {
            Id = id,
            ExecutablePath = executavel,
            Arguments = argumentos,
            AutoStart = chkAutoStart.Checked,
            Window = janela,
            TargetMonitorStableId = monitorInfo?.StableId ?? string.Empty,
            Order = _editingMetadata?.Order ?? 0,
            DelayMs = _editingMetadata?.DelayMs ?? 0,
            AskBeforeLaunch = _editingMetadata?.AskBeforeLaunch ?? false,
            RequiresNetwork = _editingMetadata?.RequiresNetwork
                ?? _original?.RequiresNetwork
                ?? false,
        };
    }

    private void InitializeCycleMetadata(IList<ProgramaConfig>? profileApps, ProgramaConfig? programa)
    {
        if (bsCycle is null || dgvCycle is null)
        {
            return;
        }

        var items = new List<ProfileItemMetadata>();

        if (profileApps is not null)
        {
            foreach (var app in profileApps)
            {
                var isTarget = programa is not null && ReferenceEquals(app, programa);
                var metadata = new ProfileItemMetadata(app, isTarget, items.Count + 1);
                items.Add(metadata);
                if (isTarget)
                {
                    _editingMetadata = metadata;
                }
            }
        }

        if (_editingMetadata is null)
        {
            var defaultOrder = items.Count > 0 ? items.Max(item => item.Order) + 1 : 1;
            var metadata = new ProfileItemMetadata(programa, isTarget: true, defaultOrder);
            items.Add(metadata);
            _editingMetadata = metadata;
        }

        _suppressCycleUpdates = true;
        try
        {
            foreach (var item in _profileItems)
            {
                item.PropertyChanged -= ProfileItem_PropertyChanged;
            }

            _profileItems.RaiseListChangedEvents = false;
            _profileItems.Clear();
            foreach (var item in items
                         .OrderBy(i => i.Order)
                         .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase))
            {
                item.PropertyChanged += ProfileItem_PropertyChanged;
                _profileItems.Add(item);
            }
            _profileItems.RaiseListChangedEvents = true;
            _profileItems.ResetBindings();

            RenumberCycleOrders();
        }
        finally
        {
            _suppressCycleUpdates = false;
        }

        bsCycle.DataSource = _profileItems;
        RefreshCyclePreviewNumbers();
        SelectCycleItem(_editingMetadata);
    }

    private void ProfileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressCycleUpdates)
        {
            return;
        }

        RefreshCyclePreviewNumbers();
    }

    private void RenumberCycleOrders()
    {
        _suppressCycleUpdates = true;
        try
        {
            for (var index = 0; index < _profileItems.Count; index++)
            {
                var item = _profileItems[index];
                item.Order = index + 1;
            }
        }
        finally
        {
            _suppressCycleUpdates = false;
        }

        RefreshCyclePreviewNumbers();
    }

    private void RefreshCyclePreviewNumbers()
    {
        if (_suppressCycleUpdates)
        {
            return;
        }

        UpdateCycleButtons();
        monitorPreviewDisplay?.Invalidate();
    }

    private void UpdateCycleButtons()
    {
        if (btnCycleUp is null || btnCycleDown is null || dgvCycle is null)
        {
            return;
        }

        if (dgvCycle.Rows.Count == 0)
        {
            btnCycleUp.Enabled = false;
            btnCycleDown.Enabled = false;
            return;
        }

        var index = dgvCycle.CurrentCell?.RowIndex ?? (dgvCycle.SelectedRows.Count > 0 ? dgvCycle.SelectedRows[0].Index : -1);
        btnCycleUp.Enabled = index > 0;
        btnCycleDown.Enabled = index >= 0 && index < dgvCycle.Rows.Count - 1;
    }

    private void SelectCycleItem(ProfileItemMetadata? item)
    {
        if (dgvCycle is null)
        {
            return;
        }

        if (item is null)
        {
            UpdateCycleButtons();
            return;
        }

        var index = _profileItems.IndexOf(item);
        if (index < 0 || index >= dgvCycle.Rows.Count)
        {
            UpdateCycleButtons();
            return;
        }

        _suppressCycleSelectionEvents = true;
        try
        {
            dgvCycle.ClearSelection();
            dgvCycle.CurrentCell = dgvCycle.Rows[index].Cells[0];
            dgvCycle.Rows[index].Selected = true;
        }
        finally
        {
            _suppressCycleSelectionEvents = false;
        }

        UpdateCycleButtons();
    }

    private void txtId_TextChanged(object? sender, EventArgs e)
    {
        if (_editingMetadata is null)
        {
            return;
        }

        _editingMetadata.Id = txtId.Text?.Trim() ?? string.Empty;
    }

    private void dgvCycle_SelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressCycleSelectionEvents)
        {
            return;
        }

        UpdateCycleButtons();
    }

    private void dgvCycle_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
    {
        if (dgvCycle?.IsCurrentCellDirty == true)
        {
            dgvCycle.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    private void dgvCycle_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
        {
            return;
        }

        if (dgvCycle is null)
        {
            return;
        }

        var columnName = dgvCycle.Columns[e.ColumnIndex].Name;
        var metadata = e.RowIndex < _profileItems.Count ? _profileItems[e.RowIndex] : null;

        if (string.Equals(columnName, "colCycleOrder", StringComparison.Ordinal))
        {
            SortCycleItemsByOrder();
            RenumberCycleOrders();
            SelectCycleItem(metadata);
        }

        RefreshCyclePreviewNumbers();
    }

    private void dgvCycle_DataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
    }

    private void SortCycleItemsByOrder()
    {
        _suppressCycleUpdates = true;
        try
        {
            var ordered = _profileItems
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _profileItems.RaiseListChangedEvents = false;
            _profileItems.Clear();
            foreach (var item in ordered)
            {
                _profileItems.Add(item);
            }
            _profileItems.RaiseListChangedEvents = true;
            _profileItems.ResetBindings();
        }
        finally
        {
            _suppressCycleUpdates = false;
        }

        RefreshCyclePreviewNumbers();
    }

    private void btnCycleUp_Click(object? sender, EventArgs e)
    {
        if (dgvCycle is null)
        {
            return;
        }

        var index = dgvCycle.CurrentCell?.RowIndex ?? (dgvCycle.SelectedRows.Count > 0 ? dgvCycle.SelectedRows[0].Index : -1);
        if (index <= 0)
        {
            return;
        }

        var item = _profileItems[index];

        _profileItems.RaiseListChangedEvents = false;
        _profileItems.RemoveAt(index);
        _profileItems.Insert(index - 1, item);
        _profileItems.RaiseListChangedEvents = true;
        _profileItems.ResetBindings();

        RenumberCycleOrders();
        SelectCycleItem(item);
    }

    private void btnCycleDown_Click(object? sender, EventArgs e)
    {
        if (dgvCycle is null)
        {
            return;
        }

        var index = dgvCycle.CurrentCell?.RowIndex ?? (dgvCycle.SelectedRows.Count > 0 ? dgvCycle.SelectedRows[0].Index : -1);
        if (index < 0 || index >= _profileItems.Count - 1)
        {
            return;
        }

        var item = _profileItems[index];

        _profileItems.RaiseListChangedEvents = false;
        _profileItems.RemoveAt(index);
        _profileItems.Insert(index + 1, item);
        _profileItems.RaiseListChangedEvents = true;
        _profileItems.ResetBindings();

        RenumberCycleOrders();
        SelectCycleItem(item);
    }

    private void CommitProfileMetadata()
    {
        if (_profileApps is null)
        {
            return;
        }

        foreach (var metadata in _profileItems)
        {
            if (metadata.Original is null)
            {
                continue;
            }

            if (_original is not null && ReferenceEquals(metadata.Original, _original))
            {
                continue;
            }

            var index = FindProfileIndex(metadata.Original);
            if (index < 0)
            {
                continue;
            }

            var current = _profileApps[index];
            _profileApps[index] = current with
            {
                Order = metadata.Order,
                DelayMs = metadata.DelayMs,
                AskBeforeLaunch = metadata.AskBeforeLaunch,
                RequiresNetwork = metadata.RequiresNetwork ?? current.RequiresNetwork,
            };
        }
    }

    private int FindProfileIndex(ProgramaConfig target)
    {
        for (var i = 0; i < _profileApps!.Count; i++)
        {
            if (ReferenceEquals(_profileApps[i], target))
            {
                return i;
            }
        }

        return -1;
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
        StopCycleSimulation();
        base.OnFormClosing(e);
        monitorPreviewDisplay?.Unbind();
    }

    private void AppsTab_ExecutableChosen(object? sender, AppSelectionEventArgs e)
    {
        if (_suppressListSelectionChanged)
        {
            return;
        }

        if (e.App is null)
        {
            _execSourceMode = ExecSourceMode.Custom;
            txtExecutavel.Text = e.ExecutablePath;
            appsTabControl!.ExecutablePath = e.ExecutablePath;
            UpdateExePreview();

            if (ClearAppsInventorySelectionMethod is not null)
            {
                try
                {
                    _suppressListSelectionChanged = true;
                    ClearAppsInventorySelectionMethod.Invoke(appsTabControl, Array.Empty<object>());
                }
                catch (TargetInvocationException)
                {
                    // Ignored: falha ao limpar a seleção não deve impedir a seleção personalizada.
                }
                finally
                {
                    _suppressListSelectionChanged = false;
                }
            }

            return;
        }

        if (_execSourceMode == ExecSourceMode.Custom)
        {
            return;
        }

        _execSourceMode = ExecSourceMode.Inventory;
        txtExecutavel.Text = e.ExecutablePath;
        appsTabControl!.ExecutablePath = e.ExecutablePath;
        UpdateExePreview();
    }

    private void AppsTab_ExecutableCleared(object? sender, EventArgs e)
    {
        if (_suppressListSelectionChanged)
        {
            return;
        }

        _execSourceMode = ExecSourceMode.None;
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
                UseWaitCursor = true;
                Cursor.Current = Cursors.WaitCursor;

                try
                {
                    await _appRunner.RunAndPositionAsync(app, monitor, bounds).ConfigureAwait(true);
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                    UseWaitCursor = false;
                }
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

    private interface IInstalledAppsProvider
    {
        Task<IReadOnlyList<InstalledAppInfo>> QueryAsync();
    }

    private sealed class RegistryInstalledAppsProvider : IInstalledAppsProvider
    {
        public Task<IReadOnlyList<InstalledAppInfo>> QueryAsync()
        {
            return Task.Run<IReadOnlyList<InstalledAppInfo>>(InstalledAppsProvider.GetAll);
        }
    }

    private sealed class SimRectDisplay : IDisposable
    {
        public SimRectDisplay(AppCycleSimulator.SimRect rect, Panel panel, Label label)
        {
            Rect = rect;
            Panel = panel;
            Label = label;
            NormalFont = label.Font;
            BoldFont = new Font(label.Font, FontStyle.Bold);
            LastResult = SimRectStatus.None;
        }

        public AppCycleSimulator.SimRect Rect { get; }

        public Panel Panel { get; }

        public Label Label { get; }

        public Font NormalFont { get; }

        public Font BoldFont { get; }

        public DateTime? LastActivation { get; set; }

        public DateTime? LastSkipped { get; set; }

        public SimRectStatus LastResult { get; set; }

        public void Dispose()
        {
            BoldFont.Dispose();
        }
    }

    private enum SimRectStatus
    {
        None,
        Completed,
        Skipped,
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

    private sealed class ProfileItemMetadata : INotifyPropertyChanged
    {
        private string _id;
        private int _order;
        private int _delayMs;
        private bool _askBeforeLaunch;
        private bool? _requiresNetwork;

        public ProfileItemMetadata(AppConfig? source, bool isTarget, int defaultOrder)
        {
            Original = source;
            IsTarget = isTarget;
            _id = source?.Id ?? string.Empty;
            var initialOrder = source?.Order ?? 0;
            _order = initialOrder > 0 ? initialOrder : Math.Max(1, defaultOrder);
            _delayMs = source?.DelayMs ?? 0;
            _askBeforeLaunch = source?.AskBeforeLaunch ?? false;
            _requiresNetwork = source?.RequiresNetwork;
        }

        public AppConfig? Original { get; }

        public bool IsTarget { get; }

        public string Id
        {
            get => _id;
            set
            {
                var normalized = value ?? string.Empty;
                if (_id != normalized)
                {
                    _id = normalized;
                    OnPropertyChanged(nameof(Id));
                }
            }
        }

        public int Order
        {
            get => _order;
            set
            {
                var normalized = value < 0 ? 0 : value;
                if (_order != normalized)
                {
                    _order = normalized;
                    OnPropertyChanged(nameof(Order));
                }
            }
        }

        public int DelayMs
        {
            get => _delayMs;
            set
            {
                var normalized = value < 0 ? 0 : value;
                if (_delayMs != normalized)
                {
                    _delayMs = normalized;
                    OnPropertyChanged(nameof(DelayMs));
                }
            }
        }

        public bool AskBeforeLaunch
        {
            get => _askBeforeLaunch;
            set
            {
                if (_askBeforeLaunch != value)
                {
                    _askBeforeLaunch = value;
                    OnPropertyChanged(nameof(AskBeforeLaunch));
                }
            }
        }

        public bool? RequiresNetwork
        {
            get => _requiresNetwork;
            set
            {
                if (_requiresNetwork != value)
                {
                    _requiresNetwork = value;
                    OnPropertyChanged(nameof(RequiresNetwork));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void UpdateFrom(AppConfig app)
        {
            Id = app.Id;
            Order = app.Order > 0 ? app.Order : Order;
            DelayMs = app.DelayMs;
            AskBeforeLaunch = app.AskBeforeLaunch;
            RequiresNetwork = app.RequiresNetwork;
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
