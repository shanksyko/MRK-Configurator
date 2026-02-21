#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Interop;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Mieruka.App.Simulation;
using Mieruka.App.Ui.PreviewBindings;
using Mieruka.Core.Config;
using Mieruka.Core.InstalledApps;
using Mieruka.Core.Models;
using Mieruka.Core.Interop;
using Mieruka.Core.Contracts;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;
using Serilog;
using Serilog.Context;

namespace Mieruka.App.Forms;

public partial class AppEditorForm : WinForms.Form, IMonitorSelectionProvider
{
    private readonly ILogger _logger = Log.ForContext<AppEditorForm>();
    private static readonly TimeSpan WindowTestTimeout = TimeSpan.FromSeconds(5);
    private const int EnumCurrentSettings = -1;
    private static readonly TimeSpan PreviewResumeDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan HoverThrottleInterval = TimeSpan.FromMilliseconds(1000d / 30d);
    private static readonly MethodInfo? ClearAppsInventorySelectionMethod =
        typeof(AppsTab).GetMethod("ClearSelection", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Drawing.Color[] SimulationPalette =
    {
        Drawing.Color.FromArgb(0x4A, 0x90, 0xE2),
        Drawing.Color.FromArgb(0x50, 0xC8, 0x8D),
        Drawing.Color.FromArgb(0xF5, 0xA6, 0x2B),
        Drawing.Color.FromArgb(0xD4, 0x6A, 0x6A),
        Drawing.Color.FromArgb(0x9B, 0x59, 0xB6),
        Drawing.Color.FromArgb(0x1A, 0xBC, 0x9C),
        Drawing.Color.FromArgb(0xE6, 0x7E, 0x22),
        Drawing.Color.FromArgb(0x2E, 0x86, 0xAB),
    };

    private enum ExecSourceMode
    {
        None,
        Inventory,
        Custom,
    }

    private readonly record struct WindowPreviewSnapshot(
        string? MonitorId,
        Drawing.Rectangle Bounds,
        bool IsFullScreen,
        bool AutoStart,
        string AppId);

    private const int MinimumOverlayDimension = 10;
    private const int MinimumPreviewSurface = 50;

    private readonly BindingList<SiteConfig> _sites;
    private readonly ProgramaConfig? _original;
    private readonly IReadOnlyList<MonitorInfo>? _providedMonitors;
    private readonly List<MonitorInfo> _monitors;
    private readonly string? _preferredMonitorId;
    private readonly IAppRunner _appRunner;
    private readonly AppTestRunner _appTestRunner;
    private readonly IList<ProgramaConfig>? _profileApps;
    private readonly BindingList<ProfileItemMetadata> _profileItems = new();
    private ProfileItemMetadata? _editingMetadata;
    private readonly int _tabAplicativosIndex;
    private readonly int _tabSitesIndex;
    private bool _suppressCycleUpdates;
    private bool _suppressCycleSelectionEvents;
    private ExecSourceMode _execSourceMode;
    private bool _suppressListSelectionChanged;
    private MonitorInfo? _selectedMonitorInfo;
    private string? _selectedMonitorId;
    private bool _suppressMonitorComboEvents;
    private bool _suppressWindowInputHandlers;
    private bool _inRebuildSimRects;
    private bool _inRebuildSimulationOverlays;
    private readonly AppCycleSimulator _cycleSimulator = new();
    private readonly List<SimRectDisplay> _cycleDisplays = new();
    private CancellationTokenSource? _cycleSimulationCts;
    private SimRectDisplay? _activeCycleDisplay;
    private int _nextSimRectIndex;
    private readonly Stopwatch _hoverSw = new();
    private Drawing.Point? _hoverPendingPoint;
    private Drawing.Point? _hoverAppliedPoint;
    private CancellationTokenSource? _hoverThrottleCts;
    private readonly SynchronizationContext? _uiContext;
    private readonly int _uiThreadId;
    private readonly IInstalledAppsProvider _installedAppsProvider = new RegistryInstalledAppsProvider();
    private bool _appsListLoaded;
    private readonly Guid _editSessionId;
    private readonly IDisposable _logScope;
    private readonly IBindingService _bindingBatchService = new BindingBatchService();
    private readonly TabEditCoordinator _tabEditCoordinator;
    private readonly Stopwatch _windowPreviewStopwatch = Stopwatch.StartNew();
    private WindowPreviewSnapshot _windowPreviewSnapshot;
    private bool _hasWindowPreviewSnapshot;
    private TimeSpan _lastWindowPreviewSnapshotAt = TimeSpan.Zero;
    private Drawing.Point? _lastWindowPreviewSnapshotPoint;
    private TimeSpan _lastWindowPreviewRebuild = TimeSpan.Zero;
    private bool _windowPreviewRebuildScheduled;
    private readonly WinForms.Timer _windowBoundsDebounce;
    private bool _windowBoundsDebouncePending;
    private Drawing.Rectangle _lastWindowRectLog = Drawing.Rectangle.Empty;
    private DateTime _lastWindowRectLogUtc;
    private readonly SemaphoreSlim _monitorPreviewGate = new(1, 1);
    private string? _monitorPreviewMonitorId;
    private Drawing.Rectangle _lastOverlayBounds = Drawing.Rectangle.Empty;
    private string? _lastOverlayMonitorId;
    private bool _lastOverlayFullScreen;
    private bool _hasCachedOverlayBounds;
    private int _invalidSnapshotAttempts;
    private Drawing.PointF? _lastClickMonitorPoint;
    private bool _settingCycleCurrentCell;
    private bool _isDirty;
    private bool _suppressDirtyTracking;

    private static int _simRectsDepth;
    private static int _simOverlaysDepth;

    private static readonly TimeSpan WindowPreviewRebuildInterval = TimeSpan.FromMilliseconds(1000d / 60d);
    private static readonly TimeSpan WindowPreviewSnapshotThrottleInterval = TimeSpan.FromMilliseconds(75);
    private const int WindowPreviewSnapshotDelta = 4;

    public AppEditorForm(
        ProgramaConfig? programa = null,
        IReadOnlyList<MonitorInfo>? monitors = null,
        string? selectedMonitorId = null,
        IAppRunner? appRunner = null,
        IList<ProgramaConfig>? profileApps = null)
    {
        _editSessionId = Guid.NewGuid();
        _logScope = LogContext.PushProperty("EditSessionId", _editSessionId);
        InitializeComponent();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        DoubleBuffered = true;
        _uiThreadId = Environment.CurrentManagedThreadId;
        _uiContext = SynchronizationContext.Current;
        ToolTipTamer.Tame(this, components);

        _windowBoundsDebounce = new WinForms.Timer(components!)
        {
            Interval = 150
        };
        _windowBoundsDebounce.Tick += WindowBoundsDebounceOnTick;

        var editorTabs = tabEditor ?? throw new InvalidOperationException("O TabControl do editor nÃ£o foi carregado.");
        var appsTabPage = tabAplicativos ?? throw new InvalidOperationException("A aba de aplicativos nÃ£o foi configurada.");
        var sitesTabPage = tabSites ?? throw new InvalidOperationException("A aba de sites nÃ£o foi configurada.");
        _ = pnlBrowserPanel ?? throw new InvalidOperationException("O painel de opÃ§Ãµes de navegador nÃ£o foi configurado.");
        var salvar = btnSalvar ?? throw new InvalidOperationException("O botÃ£o Salvar nÃ£o foi carregado.");
        _ = btnCancelar ?? throw new InvalidOperationException("O botÃ£o Cancelar nÃ£o foi carregado.");
        var sitesControl = sitesEditorControl ?? throw new InvalidOperationException("O controle de sites nÃ£o foi carregado.");
        var appsTab = appsTabControl ?? throw new InvalidOperationException("A aba de aplicativos nÃ£o foi carregada.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider nÃ£o foi configurado.");
        var cycleSource = bsCycle ?? throw new InvalidOperationException("A fonte de dados do ciclo nÃ£o foi configurada.");
        var cycleGrid = dgvCycle ?? throw new InvalidOperationException("O grid de ciclo nÃ£o foi carregado.");
        _ = tlpCycle ?? throw new InvalidOperationException("O layout do ciclo nÃ£o foi carregado.");
        _ = btnCycleUp ?? throw new InvalidOperationException("O botÃ£o de mover para cima do ciclo nÃ£o foi carregado.");
        _ = btnCycleDown ?? throw new InvalidOperationException("O botÃ£o de mover para baixo do ciclo nÃ£o foi carregado.");

        _ = rbExe ?? throw new InvalidOperationException("O seletor de executÃ¡vel nÃ£o foi carregado.");
        _ = rbBrowser ?? throw new InvalidOperationException("O seletor de navegador nÃ£o foi carregado.");
        _ = btnBrowseExe ?? throw new InvalidOperationException("O botÃ£o de procurar executÃ¡veis nÃ£o foi carregado.");
        _ = cmbBrowserEngine ?? throw new InvalidOperationException("O seletor de motor de navegador nÃ£o foi carregado.");
        _ = lblBrowserDetected ?? throw new InvalidOperationException("O rÃ³tulo de navegadores detectados nÃ£o foi carregado.");
        _ = pnlBrowserPanel ?? throw new InvalidOperationException("O painel de opÃ§Ãµes de navegador nÃ£o foi carregado.");

        _ = tlpMonitorPreview ?? throw new InvalidOperationException("O painel de prÃ©-visualizaÃ§Ã£o nÃ£o foi configurado.");
        var previewControl = monitorPreviewDisplay ?? throw new InvalidOperationException("O controle de prÃ©-visualizaÃ§Ã£o do monitor nÃ£o foi configurado.");
        previewControl.IsCoordinateAnalysisMode = true;
        previewControl.EditSessionId = _editSessionId;
        previewControl.PreviewStarted += MonitorPreviewDisplayOnPreviewStarted;
        previewControl.PreviewStopped += MonitorPreviewDisplayOnPreviewStopped;
        _ = lblMonitorCoordinates ?? throw new InvalidOperationException("O rÃ³tulo de coordenadas do monitor nÃ£o foi configurado.");
        var janelaTab = tpJanela ?? throw new InvalidOperationException("A aba de janela nÃ£o foi configurada.");

        // Health Check combo setup
        cmbHealthCheckType.SelectedIndex = 0;
        cmbHealthCheckType.SelectedIndexChanged += (_, _) => UpdateHealthCheckFieldsVisibility();
        UpdateHealthCheckFieldsVisibility();

        _tabAplicativosIndex = editorTabs.TabPages.IndexOf(appsTabPage);
        if (_tabAplicativosIndex < 0)
        {
            throw new InvalidOperationException("A aba de aplicativos nÃ£o foi adicionada ao controle de abas.");
        }

        _tabSitesIndex = editorTabs.TabPages.IndexOf(sitesTabPage);
        if (_tabSitesIndex < 0)
        {
            throw new InvalidOperationException("A aba de sites nÃ£o foi adicionada ao controle de abas.");
        }

        AcceptButton = salvar;
        CancelButton = btnCancelar;

        _appRunner = appRunner ?? new AppRunner();
        _appRunner.BeforeMoveWindow += AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow += AppRunnerOnAfterMoveWindow;
        _appTestRunner = new AppTestRunner(this, _appRunner);
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

        var gpuEnabled = GpuCaptureGuard.CanUseGpu();
        _logger.Information("UI: GPU status when opening editor: {GpuOn}", gpuEnabled);

        RefreshMonitorSnapshot();

        _sites = new BindingList<SiteConfig>();
        sitesControl.Sites = _sites;
        sitesControl.AddRequested += SitesEditorControl_AddRequested;
        sitesControl.RemoveRequested += SitesEditorControl_RemoveRequested;
        sitesControl.CloneRequested += SitesEditorControl_CloneRequested;

        _bindingBatchService.Track(_profileItems);
        _bindingBatchService.Track(_sites);
        _bindingBatchService.Track(cycleSource);
        _tabEditCoordinator = new TabEditCoordinator(
            this,
            _bindingBatchService,
            pausePreview: null,
            resumePreview: null,
            Log.ForContext<TabEditCoordinator>().ForContext("EditSessionId", _editSessionId));

        appsTab.ExecutableChosen += AppsTab_ExecutableChosen;
        appsTab.ExecutableCleared += AppsTab_ExecutableCleared;
        appsTab.ArgumentsChanged += AppsTab_ArgumentsChanged;
        appsTab.OpenRequested += AppsTab_OpenRequestedAsync;
        appsTab.TestRequested += AppsTab_TestRequestedAsync;

        txtExecutavel.TextChanged += (_, _) => UpdateExePreview();
        txtArgumentos.TextChanged += (_, _) => UpdateExePreview();
        txtId.TextChanged += txtId_TextChanged;
        txtId.Leave += (_, _) => ValidarCampoId();

        cboMonitores.SelectedIndexChanged += cboMonitores_SelectedIndexChanged;
        PopulateMonitorCombo(programa);

        previewControl.MonitorMouseMove += MonitorPreviewDisplay_OnMonitorMouseMove;
        previewControl.MonitorMouseClick += MonitorPreviewDisplay_OnMonitorMouseClick;
        previewControl.MonitorMouseLeft += MonitorPreviewDisplay_MonitorMouseLeft;

        janelaTab.SizeChanged += (_, _) => AdjustMonitorPreviewWidth();

        chkJanelaTelaCheia.CheckedChanged += chkJanelaTelaCheia_CheckedChanged;
        chkAutoStart.CheckedChanged += (_, _) => InvalidateWindowPreviewOverlay();

        if (nudJanelaX is not null)
        {
            nudJanelaX.ValueChanged += (_, _) => QueueWindowOverlayUpdate();
        }

        if (nudJanelaY is not null)
        {
            nudJanelaY.ValueChanged += (_, _) => QueueWindowOverlayUpdate();
        }

        if (nudJanelaLargura is not null)
        {
            nudJanelaLargura.ValueChanged += (_, _) => QueueWindowOverlayUpdate();
        }

        if (nudJanelaAltura is not null)
        {
            nudJanelaAltura.ValueChanged += (_, _) => QueueWindowOverlayUpdate();
        }

        AdjustMonitorPreviewWidth();
        UpdateWindowInputsState();
        UpdateMonitorCoordinateLabel(null);

        Disposed += AppEditorForm_Disposed;
        Shown += async (_, __) =>
        {
            if (rbExe?.Checked == true)
            {
                await EnsureAppsListAsync().ConfigureAwait(true);
            }
        };

        editorTabs.SelectedIndexChanged += TabEditor_SelectedIndexChanged;

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
            _ = UpdateMonitorPreviewAsync();
        }

        appsTab.ExecutablePath = txtExecutavel.Text;
        appsTab.Arguments = txtArgumentos.Text;
        UpdateExePreview();

        InitializeCycleSimulation();

        TabLayoutGuard.Attach(this);

        if (rbExe is not null && rbBrowser is not null)
        {
            var isBrowser = programa is not null && string.IsNullOrWhiteSpace(programa.ExecutablePath);
            rbBrowser.Checked = isBrowser;
            rbExe.Checked = !isBrowser;
        }

        BindInstalledBrowsers();
        ApplyAppTypeUI();
        UpdatePreviewVisibility();

        WireDirtyTracking();
        _suppressDirtyTracking = false;
        _isDirty = false;
    }

    private void MarkDirty()
    {
        if (_suppressDirtyTracking) return;
        _isDirty = true;
    }

    private void WireDirtyTracking()
    {
        _suppressDirtyTracking = true;
        txtId.TextChanged += (_, _) => MarkDirty();
        txtExecutavel.TextChanged += (_, _) => MarkDirty();
        txtArgumentos.TextChanged += (_, _) => MarkDirty();
        chkAutoStart.CheckedChanged += (_, _) => MarkDirty();
        rbExe.CheckedChanged += (_, _) => MarkDirty();
        rbBrowser.CheckedChanged += (_, _) => MarkDirty();
        chkJanelaTelaCheia.CheckedChanged += (_, _) => MarkDirty();
        nudJanelaX.ValueChanged += (_, _) => MarkDirty();
        nudJanelaY.ValueChanged += (_, _) => MarkDirty();
        nudJanelaLargura.ValueChanged += (_, _) => MarkDirty();
        nudJanelaAltura.ValueChanged += (_, _) => MarkDirty();
        cboMonitores.SelectedIndexChanged += (_, _) => MarkDirty();
        txtNomeAmigavel.TextChanged += (_, _) => MarkDirty();
        txtEnvVars.TextChanged += (_, _) => MarkDirty();
        chkWatchdogEnabled.CheckedChanged += (_, _) => MarkDirty();
        nudWatchdogGrace.ValueChanged += (_, _) => MarkDirty();
    }

    public ProgramaConfig? Resultado { get; private set; }

    public BindingList<SiteConfig> ResultadoSites => new(_sites.Select(site => site with { }).ToList());

    public string? SelectedMonitorId => _selectedMonitorId ?? (_selectedMonitorInfo is null ? null : MonitorIdentifier.Create(_selectedMonitorInfo));

    IReadOnlyList<MonitorInfo> IMonitorSelectionProvider.GetAvailableMonitors()
    {
        return _monitors.ToList();
    }

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
        ResetSnapshotPipelineState();
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

        // AvanÃ§ado
        txtNomeAmigavel.Text = programa.Name ?? string.Empty;
        txtWindowTitle.Text = janela.Title ?? string.Empty;
        chkAlwaysOnTop.Checked = janela.AlwaysOnTop;
        var wd = programa.Watchdog ?? new WatchdogSettings();
        chkWatchdogEnabled.Checked = wd.Enabled;
        nudWatchdogGrace.Value = AjustarRange(nudWatchdogGrace, wd.RestartGracePeriodSeconds);

        var hc = wd.HealthCheck;
        cmbHealthCheckType.SelectedIndex = hc is null ? 0 : (int)hc.Type;
        txtHealthCheckUrl.Text = hc?.Url ?? string.Empty;
        txtHealthCheckDomSelector.Text = hc?.DomSelector ?? string.Empty;
        txtHealthCheckContainsText.Text = hc?.ContainsText ?? string.Empty;
        nudHealthCheckInterval.Value = AjustarRange(nudHealthCheckInterval, hc?.IntervalSeconds ?? 60);
        nudHealthCheckTimeout.Value = AjustarRange(nudHealthCheckTimeout, hc?.TimeoutSeconds ?? 10);
        UpdateHealthCheckFieldsVisibility();

        var envLines = new System.Text.StringBuilder();
        if (programa.EnvironmentVariables is { Count: > 0 } envDict)
        {
            foreach (var kv in envDict)
            {
                envLines.AppendLine($"{kv.Key}={kv.Value}");
            }
        }
        txtEnvVars.Text = envLines.ToString().TrimEnd();

        UpdateWindowInputsState();
        _ = UpdateMonitorPreviewAsync();
    }

    private static decimal AjustarRange(WinForms.NumericUpDown control, int value)
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
                cycleToolTip.SetToolTip(btnCyclePlay, "Executa a simulaÃ§Ã£o do ciclo.");
            }

            if (btnCycleStep is not null)
            {
                cycleToolTip.SetToolTip(btnCycleStep, "AvanÃ§a manualmente para o prÃ³ximo item do ciclo.");
            }

            if (btnCycleStop is not null)
            {
                cycleToolTip.SetToolTip(btnCycleStop, "Interrompe a simulaÃ§Ã£o atual.");
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
        using var scope = _tabEditCoordinator.BeginEditScope(nameof(ApplyAppTypeUI));
        if (!scope.IsActive)
        {
            _logger.Debug("ApplyAppTypeUI: exit scope inactive");
            return;
        }

        var isExecutable = rbExe?.Checked ?? false;
        var isBrowser = rbBrowser?.Checked ?? false;
        _logger.Debug(
            "ApplyAppTypeUI: enter isExecutable={IsExecutable} isBrowser={IsBrowser}",
            isExecutable,
            isBrowser);

        var tabControl = tabEditor;
        var appsTabPage = tabAplicativos;
        var sitesTabPage = tabSites;

        if (tabControl is not null && appsTabPage is not null && sitesTabPage is not null)
        {
            SetTabVisibility(tabControl, appsTabPage, _tabAplicativosIndex, isExecutable);
            SetTabVisibility(tabControl, sitesTabPage, _tabSitesIndex, !isExecutable);

            if (isExecutable && ReferenceEquals(tabControl.SelectedTab, sitesTabPage))
            {
                tabControl.SelectedTab = tabControl.TabPages.Contains(appsTabPage)
                    ? appsTabPage
                    : (tabControl.TabPages.Count > 0 ? tabControl.TabPages[0] : null);
            }
            else if (!isExecutable && ReferenceEquals(tabControl.SelectedTab, appsTabPage))
            {
                tabControl.SelectedTab = tabControl.TabPages.Contains(sitesTabPage)
                    ? sitesTabPage
                    : (tabControl.TabPages.Count > 0 ? tabControl.TabPages[0] : null);
            }

            if (tabControl.SelectedTab is not null && !tabControl.TabPages.Contains(tabControl.SelectedTab))
            {
                tabControl.SelectedTab = tabControl.TabPages.Count > 0 ? tabControl.TabPages[0] : null;
            }
        }

        if (appsTabControl is not null)
        {
            appsTabControl.Enabled = isExecutable;
        }

        if (btnBrowseExe is not null)
        {
            btnBrowseExe.Visible = isExecutable;
            btnBrowseExe.Enabled = isExecutable;
            btnBrowseExe.TabStop = isExecutable;
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

            if (pnlBrowserPanel is not null)
            {
                pnlBrowserPanel.Enabled = false;
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

        if (pnlBrowserPanel is not null)
        {
            pnlBrowserPanel.Visible = isBrowser;
        }

        if (sitesEditorControl is not null)
        {
            sitesEditorControl.Visible = isBrowser;
        }

        var selectedTabName = tabEditor?.SelectedTab?.Name ?? tabEditor?.SelectedTab?.Text ?? string.Empty;
        _logger.Information(
            "ApplyAppTypeUI: exit selectedTab={SelectedTab} isExecutable={IsExecutable} isBrowser={IsBrowser}",
            selectedTabName,
            isExecutable,
            isBrowser);
    }

    private static void SetTabVisibility(TabControl tabControl, TabPage tabPage, int originalIndex, bool visible)
    {
        var pages = tabControl.TabPages;
        var contains = pages.Contains(tabPage);

        if (visible)
        {
            if (contains)
            {
                return;
            }

            var insertIndex = originalIndex;
            if (insertIndex < 0 || insertIndex > pages.Count)
            {
                insertIndex = pages.Count;
            }

            pages.Insert(insertIndex, tabPage);
        }
        else if (contains)
        {
            pages.Remove(tabPage);
        }
    }

    private async Task EnsureAppsListAsync()
    {
        if (rbExe?.Checked != true)
        {
            return;
        }

        if (_appsListLoaded)
        {
            return;
        }

        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _appsListLoaded = true;

        UseWaitCursor = true;
        var previousCursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;

        try
        {
            var apps = await _installedAppsProvider.QueryAsync().ConfigureAwait(true);
            appsTabControl?.SetInstalledApps(apps);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Falha ao carregar a lista de aplicativos instalados.");
            _appsListLoaded = false;
        }
        finally
        {
            Cursor.Current = previousCursor;
            UseWaitCursor = false;
            ApplyAppTypeUI();
        }
    }

    private void BindInstalledBrowsers()
    {
        if (cmbNavegadores is null)
        {
            return;
        }

        var browsers = BrowserDiscovery.GetInstalledBrowsers().ToList();

        cmbNavegadores.DisplayMember = nameof(BrowserInfo.Name);
        cmbNavegadores.ValueMember = nameof(BrowserInfo.ExecutablePath);
        cmbNavegadores.DataSource = browsers;
        cmbNavegadores.Enabled = browsers.Count > 0;

        if (browsers.Count > 0)
        {
            cmbNavegadores.SelectedIndex = 0;
        }
        else
        {
            cmbNavegadores.SelectedIndex = -1;
        }
    }

    private void BindDetectedBrowsers()
    {
        if (cmbBrowserEngine is null || lblBrowserDetected is null || pnlBrowserPanel is null)
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
        pnlBrowserPanel.Enabled = hasSupported;

        if (sitesEditorControl is not null)
        {
            sitesEditorControl.Enabled = hasSupported;
        }
    }

    private void cmbNavegadores_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cmbNavegadores?.SelectedItem is not BrowserInfo browser)
        {
            return;
        }

        if (txtExecutavel is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(browser.ExecutablePath))
        {
            return;
        }

        txtExecutavel.Text = browser.ExecutablePath;
        txtExecutavel.SelectionStart = txtExecutavel.TextLength;
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

            builder.Append("NÃ£o encontrados: ");
            builder.Append(string.Join(", ", supportedMissing));
            builder.Append('.');
        }

        if (unsupportedDetected.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append("Detectados (nÃ£o suportados): ");
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
                : $"{Installation.DisplayName} (nÃ£o encontrado)";
        }
    }

    private void Sites_ListChanged(object? sender, ListChangedEventArgs e)
    {
        RebuildSimRects();
    }

    private void RebuildSimRects()
    {
        using var logicalDepth = new MonitorPreviewDisplay.PreviewLogicalScope(nameof(RebuildSimRects), _logger);
        if (!logicalDepth.Entered)
        {
            _logger.Error("RebuildSimRects: preview logical depth limit reached; aborting rebuild");
            return;
        }

        using var depth = new MonitorPreviewDisplay.PreviewCallScope(nameof(RebuildSimRects), _logger);
        if (!depth.Entered)
        {
            _logger.Error("RebuildSimRects: depth limit reached; aborting rebuild");
            return;
        }

        var currentDepth = Interlocked.Increment(ref _simRectsDepth);
        if (currentDepth > 8)
        {
            _logger.Error("sim_rects_depth_limit_reached depth={Depth} limit={Limit}", currentDepth, 8);
            Interlocked.Decrement(ref _simRectsDepth);
            return;
        }

        if (_inRebuildSimRects)
        {
            _logger.Debug("RebuildSimRects: recursion blocked");
            Interlocked.Decrement(ref _simRectsDepth);
            return;
        }

        _inRebuildSimRects = true;
        try
        {
            _logger.Debug(
                "RebuildSimRects: enter isDisposed={IsDisposed} simulationActive={SimulationActive}",
                IsDisposed,
                _cycleSimulationCts is not null);

            if (IsDisposed)
            {
                _logger.Debug("RebuildSimRects: exit form disposed");
                return;
            }

            if (_cycleSimulationCts is not null)
            {
                StopCycleSimulation();

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(RebuildSimRects));
                }

                _logger.Information("RebuildSimRects: exit deferred due to active simulation");
                return;
            }

            _nextSimRectIndex = 0;
            ClearActiveSimRect();

            if (flowCycleItems is null)
            {
                _logger.Information("RebuildSimRects: exit missing cycle container");
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
                var placeholder = new WinForms.Label
                {
                    AutoSize = true,
                    Margin = new WinForms.Padding(12),
                    Text = "Nenhum item disponÃ­vel para simulaÃ§Ã£o.",
                };

                flowCycleItems.Controls.Add(placeholder);
                UpdateCycleControlsState();
                _logger.Information("RebuildSimRects: exit no items generated");
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
            _logger.Information(
                "RebuildSimRects: exit createdDisplays={DisplayCount} totalItems={ItemCount}",
                _cycleDisplays.Count,
                items.Count);
        }
        finally
        {
            _inRebuildSimRects = false;
            var depthAfter = Interlocked.Decrement(ref _simRectsDepth);
            if (depthAfter < 0)
            {
                Interlocked.Exchange(ref _simRectsDepth, 0);
            }
        }
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
        var panel = new WinForms.Panel
        {
            Width = 200,
            Height = 120,
            Margin = new WinForms.Padding(12),
            Padding = new WinForms.Padding(4),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.ControlLightLight,
        };

        var label = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = rect.DisplayName,
            AutoEllipsis = true,
            Padding = new WinForms.Padding(4),
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
        builder.Append("Rede necessÃ¡ria: ");
        builder.Append(display.Rect.RequiresNetwork ? "Sim" : "NÃ£o");
        builder.AppendLine();
        builder.Append("DuraÃ§Ã£o simulada: ");
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
            builder.Append("Ãšltima execuÃ§Ã£o: ");
            builder.Append(completed.ToString("T", CultureInfo.CurrentCulture));
        }
        else if (display.LastResult == SimRectStatus.Skipped && display.LastSkipped is DateTime skipped)
        {
            builder.AppendLine();
            builder.Append("Ignorado Ã s ");
            builder.Append(skipped.ToString("T", CultureInfo.CurrentCulture));
            builder.Append(" (rede indisponÃ­vel)");
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
            // Ignorar cancelamentos solicitados pelo usuÃ¡rio.
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
            display.Panel.BackColor = Drawing.Color.FromArgb(32, 146, 204);
            display.Label.ForeColor = Drawing.Color.White;
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
                // Ignorar falhas durante o descarte dos painÃ©is.
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

    private async void rbExe_CheckedChanged(object? sender, EventArgs e)
    {
        ApplyAppTypeUI();
        await EnsureAppsListAsync().ConfigureAwait(true);
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

        _ = UpdateMonitorPreviewAsync();
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

    private void TabEditor_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdatePreviewVisibility();
    }

    private async void cboMonitores_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressMonitorComboEvents)
        {
            return;
        }

        await UpdateMonitorPreviewSafelyAsync().ConfigureAwait(true);
    }

    private async Task UpdateMonitorPreviewSafelyAsync()
    {
        try
        {
            await UpdateMonitorPreviewAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "UpdateMonitorPreviewSafelyAsync: monitor preview update failed");
        }
    }

    private async Task UpdateMonitorPreviewAsync()
    {
        await _monitorPreviewGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var previousMonitor = _selectedMonitorInfo;
            var previousMonitorId = _selectedMonitorId;
            var previousWindow = BuildWindowConfigurationFromInputs();

            _selectedMonitorInfo = null;
            _selectedMonitorId = null;

            if (cboMonitores?.SelectedItem is not MonitorOption option || option.Monitor is null || option.MonitorId is null)
            {
                if (monitorPreviewDisplay is not null)
                {
                    await monitorPreviewDisplay.UnbindAsync().ConfigureAwait(true);
                }

                _monitorPreviewMonitorId = null;
                UpdateMonitorCoordinateLabel(null);
                monitorPreviewDisplay?.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
                return;
            }

            _selectedMonitorInfo = option.Monitor;
            _selectedMonitorId = option.MonitorId;

            var monitorChanged = !string.Equals(_monitorPreviewMonitorId, option.MonitorId, StringComparison.OrdinalIgnoreCase);

            if (previousMonitor is not null &&
                !string.Equals(previousMonitorId, option.MonitorId, StringComparison.OrdinalIgnoreCase))
            {
                ApplyRelativeWindowToNewMonitor(previousWindow, previousMonitor, option.Monitor);
            }

            if (monitorChanged && monitorPreviewDisplay is not null)
            {
                ResetSnapshotPipelineState();
                await monitorPreviewDisplay.BindAsync(option.Monitor, autoStart: false).ConfigureAwait(true);
                _monitorPreviewMonitorId = option.MonitorId;
            }

            UpdateMonitorCoordinateLabel(null);
            RebuildSimulationOverlays();
        }
        finally
        {
            _monitorPreviewGate.Release();
        }
    }

    private void chkJanelaTelaCheia_CheckedChanged(object? sender, EventArgs e)
    {
        UpdateWindowInputsState();

        _ = ClampWindowInputsToMonitor(null, allowFullScreen: true);
        QueueWindowOverlayUpdate();
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

    private void UpdateMonitorCoordinateLabel(Drawing.PointF? coordinates)
    {
        if (lblMonitorCoordinates is not { IsDisposed: false } label)
        {
            return;
        }

        var text = coordinates is null
            ? "X=â€“, Y=â€“"
            : $"X={coordinates.Value.X}, Y={coordinates.Value.Y}";

        if (!string.Equals(label.Text, text, StringComparison.Ordinal))
        {
            label.Text = text;
        }
    }

    private MonitorPreviewDisplay? TryGetPreviewControl()
    {
        return monitorPreviewDisplay;
    }

    private void MonitorPreviewDisplay_OnMonitorMouseMove(object? sender, MonitorPreviewDisplay.MonitorMouseEventArgs e)
    {
        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; ignoring mouse interaction.");
            return;
        }

        if (preview.IsInteractionSuppressed)
        {
            return;
        }

        UpdateMonitorCoordinateLabel(e.MonitorPoint);
    }

    private void MonitorPreviewDisplay_OnMonitorMouseClick(object? sender, MonitorPreviewDisplay.MonitorMouseEventArgs e)
    {
        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; ignoring mouse interaction.");
            return;
        }

        if (preview.IsInteractionSuppressed)
        {
            return;
        }

        UpdateMonitorCoordinateLabel(e.MonitorPoint);
        _lastClickMonitorPoint = e.MonitorPoint;
    }

    private void MonitorPreviewDisplay_MonitorMouseLeft(object? sender, EventArgs e)
    {
        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; ignoring mouse interaction.");
            return;
        }

        UpdateMonitorCoordinateLabel(null);

        _hoverPendingPoint = null;
        _hoverAppliedPoint = null;
        _hoverSw.Reset();
        CancelHoverThrottleTimer();

        if (preview.IsPreviewRunning)
        {
            _logger.Debug("Action skipped because live preview is running.");

            _windowBoundsDebounce?.Stop();
            _windowBoundsDebouncePending = false;
            _windowPreviewRebuildScheduled = false;

            return;
        }

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
        _ = FlushHoverPointAsync(delay, source.Token);
    }

    private async Task FlushHoverPointAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
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

        if (token.IsCancellationRequested)
        {
            return;
        }

        MarshalToUi(() =>
        {
            if (token.IsCancellationRequested || IsDisposed)
            {
                return;
            }

            CancelHoverThrottleTimer();
            ApplyPendingHoverPoint(enforceInterval: true);
        });
    }

    private void ApplyPendingHoverPoint(bool enforceInterval)
    {
        if (!IsOnUiThread())
        {
            MarshalToUi(() => ApplyPendingHoverPoint(enforceInterval));
            return;
        }

        var preview = monitorPreviewDisplay;
        if (preview is null || preview.IsDisposed || preview.IsInteractionSuppressed)
        {
            return;
        }

        if (_hoverPendingPoint is not Drawing.Point pending)
        {
            return;
        }

        if (!TryGetWindowInputs(out var xInput, out var yInput, out var widthInput, out var heightInput))
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

        if (_hoverAppliedPoint is Drawing.Point applied && applied == pending)
        {
            _hoverSw.Restart();
            return;
        }

        _hoverSw.Restart();
        _hoverAppliedPoint = pending;
        _ = ClampWindowInputsToMonitor(pending, windowInputs: (xInput, yInput, widthInput, heightInput));
    }

    private void MarshalToUi(Action action)
    {
        if (action is null || IsDisposed)
        {
            return;
        }

        void InvokeSafely()
        {
            if (IsDisposed)
            {
                return;
            }

            try
            {
                action();
            }
            catch (ObjectDisposedException)
            {
                // Ignore callbacks that race with disposal.
            }
            catch (InvalidOperationException ex) when (IsDisposed || !IsHandleCreated)
            {
                _logger.Debug(ex, "Callback ignorado devido ao controle estar indisponÃ­vel.");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Falha ao processar callback de UI agendado.");
            }
        }

        var callback = new WinForms.MethodInvoker(InvokeSafely);

        if (!IsOnUiThread())
        {
            var context = _uiContext;
            if (context is not null)
            {
                try
                {
                    context.Post(static state =>
                    {
                        if (state is WinForms.MethodInvoker invoker)
                        {
                            invoker();
                        }
                    }, callback);
                }
                catch (ObjectDisposedException)
                {
                    // Ignore context disposal during shutdown.
                }
                catch (InvalidOperationException)
                {
                    // Ignore marshaling failures when the context is no longer available.
                }

                return;
            }

            try
            {
                BeginInvoke(callback);
            }
            catch (ObjectDisposedException)
            {
                // Ignore marshaling failures after disposal.
            }
            catch (InvalidOperationException)
            {
                // Ignore marshaling failures when the window handle is gone.
            }

            return;
        }

        InvokeSafely();
    }

    private bool IsOnUiThread()
    {
        return Environment.CurrentManagedThreadId == _uiThreadId;
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
            // Ignorar cancelamentos apÃ³s descarte.
        }
        catch (AggregateException)
        {
            // Ignorar cancelamentos concorrentes.
        }

        pending.Dispose();
    }

    private bool ClampWindowInputsToMonitor(
        Drawing.Point? pointer,
        bool allowFullScreen = false,
        (WinForms.NumericUpDown X, WinForms.NumericUpDown Y, WinForms.NumericUpDown Width, WinForms.NumericUpDown Height)? windowInputs = null)
    {
        var fullScreenToggle = chkJanelaTelaCheia;
        if (!allowFullScreen && fullScreenToggle is not null && !fullScreenToggle.IsDisposed && fullScreenToggle.Checked)
        {
            return false;
        }

        (WinForms.NumericUpDown X, WinForms.NumericUpDown Y, WinForms.NumericUpDown Width, WinForms.NumericUpDown Height) inputs;
        if (windowInputs is { } provided)
        {
            inputs = provided;
        }
        else if (!TryGetWindowInputs(out var xInput, out var yInput, out var widthInput, out var heightInput))
        {
            return false;
        }
        else
        {
            inputs = (xInput, yInput, widthInput, heightInput);
        }

        var (xControl, yControl, widthControl, heightControl) = inputs;

        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            return false;
        }

        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
        var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

        var changed = false;

        _suppressWindowInputHandlers = true;
        using var redrawScope = RedrawScope.Begin(xControl, yControl, widthControl, heightControl);
        try
        {
            var width = (int)widthControl.Value;
            if (monitorWidth > 0)
            {
                var clampedWidth = Math.Clamp(width, 1, monitorWidth);
                changed |= UpdateNumericControl(widthControl, clampedWidth);
                width = clampedWidth;
            }

            var height = (int)heightControl.Value;
            if (monitorHeight > 0)
            {
                var clampedHeight = Math.Clamp(height, 1, monitorHeight);
                changed |= UpdateNumericControl(heightControl, clampedHeight);
                height = clampedHeight;
            }

            if (pointer is Drawing.Point target)
            {
                var targetX = target.X;
                if (monitorWidth > 0)
                {
                    var maxX = Math.Max(0, monitorWidth - width);
                    targetX = Math.Clamp(targetX, 0, maxX);
                }

                var targetY = target.Y;
                if (monitorHeight > 0)
                {
                    var maxY = Math.Max(0, monitorHeight - height);
                    targetY = Math.Clamp(targetY, 0, maxY);
                }

                changed |= UpdateNumericControl(xControl, targetX);
                changed |= UpdateNumericControl(yControl, targetY);
            }
            else
            {
                if (monitorWidth > 0)
                {
                    var currentX = (int)xControl.Value;
                    var maxX = Math.Max(0, monitorWidth - width);
                    var clampedX = Math.Clamp(currentX, 0, maxX);
                    changed |= UpdateNumericControl(xControl, clampedX);
                }

                if (monitorHeight > 0)
                {
                    var currentY = (int)yControl.Value;
                    var maxY = Math.Max(0, monitorHeight - height);
                    var clampedY = Math.Clamp(currentY, 0, maxY);
                    changed |= UpdateNumericControl(yControl, clampedY);
                }
            }
        }
        finally
        {
            _suppressWindowInputHandlers = false;
        }

        if (changed)
        {
            InvalidateWindowPreviewOverlay();
        }

        return changed;
    }

    private bool TryGetWindowInputs(
        out WinForms.NumericUpDown xInput,
        out WinForms.NumericUpDown yInput,
        out WinForms.NumericUpDown widthInput,
        out WinForms.NumericUpDown heightInput)
    {
        if (nudJanelaX is WinForms.NumericUpDown x && !x.IsDisposed &&
            nudJanelaY is WinForms.NumericUpDown y && !y.IsDisposed &&
            nudJanelaLargura is WinForms.NumericUpDown width && !width.IsDisposed &&
            nudJanelaAltura is WinForms.NumericUpDown height && !height.IsDisposed)
        {
            xInput = x;
            yInput = y;
            widthInput = width;
            heightInput = height;
            return true;
        }

        xInput = null!;
        yInput = null!;
        widthInput = null!;
        heightInput = null!;
        return false;
    }

    private static bool UpdateNumericControl(WinForms.NumericUpDown control, int value)
    {
        var adjusted = AjustarRange(control, value);
        if (control.Value != adjusted)
        {
            control.Value = adjusted;
            return true;
        }

        return false;
    }

    private sealed class RedrawScope : IDisposable
    {
        private const int WmSetRedraw = 0x000B;
        private readonly WinForms.Control[] _controls;

        private RedrawScope(WinForms.Control[] controls)
        {
            _controls = controls;

            foreach (var control in controls)
            {
                if (control.IsHandleCreated)
                {
                    SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }

        public static RedrawScope Begin(params WinForms.Control[] controls)
        {
            return new RedrawScope(controls);
        }

        public void Dispose()
        {
            foreach (var control in _controls)
            {
                if (!control.IsHandleCreated)
                {
                    continue;
                }

                SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                control.Invalidate();
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    private void InvalidateWindowPreviewOverlay()
    {
        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; skipping window/overlay update.");
            return;
        }

        if (preview.IsDisposed)
        {
            _windowBoundsDebounce.Stop();
            _windowBoundsDebouncePending = false;
            _windowPreviewRebuildScheduled = false;
            return;
        }

        _windowBoundsDebounce.Stop();
        _windowBoundsDebouncePending = false;

        if (_invalidSnapshotAttempts >= 5)
        {
            _logger.Debug("CaptureWindowPreviewSnapshot skipped_due_to_invalid_bounds_limit");
            return;
        }

        if (preview.IsPreviewRunning)
        {
            _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

            _windowBoundsDebounce?.Stop();
            _windowBoundsDebouncePending = false;
            _windowPreviewRebuildScheduled = false;

            return;
        }

        var snapshot = CaptureWindowPreviewSnapshot();
        var now = _windowPreviewStopwatch.Elapsed;

        if (!_hasWindowPreviewSnapshot)
        {
            ApplyWindowPreviewSnapshot(snapshot, now);
            return;
        }

        if (snapshot.Equals(_windowPreviewSnapshot))
        {
            if (_windowPreviewRebuildScheduled)
            {
                preview.Invalidate();
            }

            return;
        }

        var elapsed = now - _lastWindowPreviewRebuild;
        if (elapsed < WindowPreviewRebuildInterval)
        {
            if (!_windowPreviewRebuildScheduled)
            {
                _windowPreviewRebuildScheduled = true;
                var delay = WindowPreviewRebuildInterval - elapsed;
                ScheduleWindowPreviewRebuild(delay);
            }

            preview.Invalidate();
            return;
        }

        ApplyWindowPreviewSnapshot(snapshot, now);
    }

    private void QueueWindowOverlayUpdate()
    {
        if (_suppressWindowInputHandlers)
        {
            return;
        }

        _ = ClampWindowInputsToMonitor(null, allowFullScreen: true);

        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; skipping window/overlay update.");
            return;
        }

        if (preview.IsPreviewRunning)
        {
            _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

            _windowBoundsDebounce?.Stop();
            _windowBoundsDebouncePending = false;
            _windowPreviewRebuildScheduled = false;
            return;
        }

        var snapshot = CaptureWindowPreviewSnapshot();
        var bounds = snapshot.Bounds;
        var nowUtc = DateTime.UtcNow;
        if (!bounds.Equals(_lastWindowRectLog) || (nowUtc - _lastWindowRectLogUtc).TotalMilliseconds >= 500)
        {
            _logger.Debug(
                "RectValueChanged: x={X}, y={Y}, w={W}, h={H}",
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height);
            _lastWindowRectLog = bounds;
            _lastWindowRectLogUtc = nowUtc;
        }

        _windowBoundsDebouncePending = true;
        _windowBoundsDebounce.Stop();
        _windowBoundsDebounce.Start();
    }

    private void MonitorPreviewDisplayOnPreviewStarted(object? sender, EventArgs e)
    {
        _logger.Debug("monitor_preview_started: disabling snapshot pipeline");
        ResetSnapshotPipelineState();
    }

    private void MonitorPreviewDisplayOnPreviewStopped(object? sender, EventArgs e)
    {
        _logger.Debug("monitor_preview_stopped: re-enabling snapshot pipeline");
        ResetSnapshotPipelineState();
        InvalidateWindowPreviewOverlay();
    }

    private void WindowBoundsDebounceOnTick(object? sender, EventArgs e)
    {
        _windowBoundsDebounce.Stop();

        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; skipping window/overlay update.");
            return;
        }

        if (preview.IsPreviewRunning)
        {
            _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

            _windowBoundsDebounce?.Stop();
            _windowBoundsDebouncePending = false;
            _windowPreviewRebuildScheduled = false;
            return;
        }

        if (!_windowBoundsDebouncePending)
        {
            return;
        }

        _windowBoundsDebouncePending = false;
        InvalidateWindowPreviewOverlay();
    }

    private void ResetSnapshotPipelineState()
    {
        _invalidSnapshotAttempts = 0;
        _windowBoundsDebounce.Stop();
        _windowBoundsDebouncePending = false;
        _windowPreviewRebuildScheduled = false;
    }

    private WindowPreviewSnapshot CaptureWindowPreviewSnapshot()
    {
        var preview = TryGetPreviewControl();
        if (preview == null)
        {
            _logger.Debug("Preview control null; skipping snapshot.");
            return _windowPreviewSnapshot;
        }

        if (preview.IsPreviewRunning)
        {
            _logger.Debug("CaptureWindowPreviewSnapshot skipped_due_to_live_preview");

            _windowBoundsDebounce?.Stop();
            _windowBoundsDebouncePending = false;
            _windowPreviewRebuildScheduled = false;
            return _windowPreviewSnapshot;
        }

        using var logicalDepth = new MonitorPreviewDisplay.PreviewLogicalScope(nameof(CaptureWindowPreviewSnapshot), _logger);
        if (!logicalDepth.Entered)
        {
            _logger.Error("CaptureWindowPreviewSnapshot: preview logical depth limit reached; aborting snapshot");
            return _windowPreviewSnapshot;
        }

        using var depth = new MonitorPreviewDisplay.PreviewCallScope(nameof(CaptureWindowPreviewSnapshot), _logger);
        if (!depth.Entered)
        {
            _logger.Error("CaptureWindowPreviewSnapshot: depth limit reached; aborting snapshot");
            return _windowPreviewSnapshot;
        }

        var now = _windowPreviewStopwatch.Elapsed;
        var monitor = GetSelectedMonitor();
        var monitorId = monitor is null ? null : MonitorIdentifier.Create(monitor);
        var monitorBounds = ResolveMonitorBounds(monitor);

        if (monitorId is not null && (monitorBounds.Width < MinimumOverlayDimension || monitorBounds.Height < MinimumOverlayDimension))
        {
            _logger.Warning(
                "CaptureWindowPreviewSnapshot: monitor sem superfÃ­cie vÃ¡lida width={Width} height={Height} monitorId={MonitorId}",
                monitorBounds.Width,
                monitorBounds.Height,
                monitorId);
            monitorId = null;
        }

        var autoStart = chkAutoStart?.Checked ?? false;
        var isFullScreen = chkJanelaTelaCheia?.Checked ?? false;
        var appId = txtId?.Text?.Trim() ?? string.Empty;

        var bounds = Drawing.Rectangle.Empty;
        if (nudJanelaX is not null &&
            nudJanelaY is not null &&
            nudJanelaLargura is not null &&
            nudJanelaAltura is not null)
        {
            bounds = new Drawing.Rectangle(
                (int)nudJanelaX.Value,
                (int)nudJanelaY.Value,
                (int)nudJanelaLargura.Value,
                (int)nudJanelaAltura.Value);
        }

        if (bounds.Width < MinimumOverlayDimension || bounds.Height < MinimumOverlayDimension)
        {
            _invalidSnapshotAttempts++;
            _logger.Debug(
                "snapshot_invalid_bounds bounds={Bounds} attempts={Attempts}",
                bounds,
                _invalidSnapshotAttempts);

            if (_invalidSnapshotAttempts >= 5)
            {
                _windowBoundsDebounce.Stop();
                _windowBoundsDebouncePending = false;
                _windowPreviewRebuildScheduled = false;
                _logger.Warning(
                    "snapshot_invalid_bounds_limit_reached; disabling snapshot pipeline until reset.");
            }

            return _windowPreviewSnapshot;
        }

        _invalidSnapshotAttempts = 0;

        var currentPoint = bounds.Location;
        if (_hasWindowPreviewSnapshot)
        {
            var elapsed = now - _lastWindowPreviewSnapshotAt;
            var lastPoint = _lastWindowPreviewSnapshotPoint;
            if (elapsed < WindowPreviewSnapshotThrottleInterval &&
                lastPoint.HasValue &&
                Math.Abs(currentPoint.X - lastPoint.Value.X) < WindowPreviewSnapshotDelta &&
                Math.Abs(currentPoint.Y - lastPoint.Value.Y) < WindowPreviewSnapshotDelta)
            {
                _logger.Debug(
                    "CaptureWindowPreviewSnapshot: throttled elapsed={Elapsed} delta=({DeltaX},{DeltaY})",
                    elapsed,
                    currentPoint.X - lastPoint.Value.X,
                    currentPoint.Y - lastPoint.Value.Y);

                return _windowPreviewSnapshot;
            }
        }

        var snapshot = new WindowPreviewSnapshot(monitorId, bounds, isFullScreen, autoStart, appId);
        _lastWindowPreviewSnapshotAt = now;
        _lastWindowPreviewSnapshotPoint = currentPoint;

        _logger.Debug(
            "CaptureWindowPreviewSnapshot: exit bounds={Bounds} monitorId={MonitorId} fullScreen={FullScreen} autoStart={AutoStart} appId={AppId}",
            snapshot.Bounds,
            snapshot.MonitorId ?? string.Empty,
            snapshot.IsFullScreen,
            snapshot.AutoStart,
            snapshot.AppId);

        return snapshot;
    }

    private Drawing.Rectangle ResolveMonitorBounds(MonitorInfo? monitor)
    {
        if (monitor is null)
        {
            return Drawing.Rectangle.Empty;
        }

        if (monitor.Bounds.Width >= MinimumOverlayDimension && monitor.Bounds.Height >= MinimumOverlayDimension)
        {
            return monitor.Bounds;
        }

        if (TryGetDisplaySettings(monitor.DeviceName, out var rect))
        {
            _logger.Debug(
                "CaptureWindowPreviewSnapshot: recalculou bounds via EnumDisplaySettings width={Width} height={Height} monitor={MonitorId}",
                rect.Width,
                rect.Height,
                monitor.DeviceName);
            return rect;
        }

        try
        {
            foreach (var screen in WinForms.Screen.AllScreens)
            {
                if (string.Equals(screen.DeviceName, monitor.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                    screen.Bounds.Width >= MinimumOverlayDimension &&
                    screen.Bounds.Height >= MinimumOverlayDimension)
                {
                    return screen.Bounds;
                }
            }
        }
        catch
        {
            // Best effort only; diagnostics will handle invalid bounds.
        }

        _logger.Debug(
            "invalid_bounds_detected width={Width} height={Height} monitorId={MonitorId} source=capture_snapshot",
            monitor.Bounds.Width,
            monitor.Bounds.Height,
            monitor.DeviceName ?? string.Empty);
        return Drawing.Rectangle.Empty;
    }

    private static bool TryGetDisplaySettings(string? deviceName, out Drawing.Rectangle bounds)
    {
        bounds = Drawing.Rectangle.Empty;

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        try
        {
            var mode = new DEVMODE
            {
                dmSize = (short)Marshal.SizeOf<DEVMODE>(),
            };

            if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode))
            {
                return false;
            }

            bounds = new Drawing.Rectangle(mode.dmPositionX, mode.dmPositionY, mode.dmPelsWidth, mode.dmPelsHeight);
            return bounds.Width >= MinimumOverlayDimension && bounds.Height >= MinimumOverlayDimension;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private void ApplyWindowPreviewSnapshot(WindowPreviewSnapshot snapshot, TimeSpan timestamp)
    {
        _windowPreviewSnapshot = snapshot;
        _hasWindowPreviewSnapshot = true;
        _lastWindowPreviewRebuild = timestamp;
        _windowPreviewRebuildScheduled = false;

        if (snapshot.Bounds.Width < MinimumOverlayDimension || snapshot.Bounds.Height < MinimumOverlayDimension)
        {
            _logger.Debug(
                "ApplySelectionOverlay: ignored_zero_bound_overlay bounds={Bounds} monitor={MonitorId}",
                snapshot.Bounds,
                snapshot.MonitorId ?? string.Empty);
            return;
        }

        if (ShouldApplyOverlay(snapshot))
        {
            CacheOverlaySnapshot(snapshot);
            RebuildSimulationOverlays();
            _logger.Debug(
                "ApplySelectionOverlay: bounds={Bounds} monitor={MonitorId} fullScreen={FullScreen} autoStart={AutoStart}",
                snapshot.Bounds,
                snapshot.MonitorId ?? string.Empty,
                snapshot.IsFullScreen,
                snapshot.AutoStart);
            monitorPreviewDisplay?.Invalidate();
        }
    }

    private async void ScheduleWindowPreviewRebuild(TimeSpan delay)
    {
        try
        {
            var preview = TryGetPreviewControl();
            if (preview == null)
            {
                _logger.Debug("Preview control null; skipping window/overlay update.");
                _windowPreviewRebuildScheduled = false;
                return;
            }

            if (preview.IsDisposed)
            {
                _windowPreviewRebuildScheduled = false;
                return;
            }

            if (preview.IsPreviewRunning)
            {
                _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

                _windowBoundsDebounce?.Stop();
                _windowBoundsDebouncePending = false;
                _windowPreviewRebuildScheduled = false;
                return;
            }

            try
            {
                if (delay < TimeSpan.Zero)
                {
                    delay = TimeSpan.Zero;
                }

                await Task.Delay(delay).ConfigureAwait(true);
            }
            catch
            {
                _windowPreviewRebuildScheduled = false;
                return;
            }

            if (IsDisposed)
            {
                _windowPreviewRebuildScheduled = false;
                return;
            }

            if (preview.IsPreviewRunning)
            {
                _logger.Debug("WindowOverlayUpdate skipped_due_to_live_preview");

                _windowBoundsDebounce?.Stop();
                _windowBoundsDebouncePending = false;
                _windowPreviewRebuildScheduled = false;
                return;
            }

            _windowPreviewRebuildScheduled = false;
            InvalidateWindowPreviewOverlay();
        }
        catch (Exception ex)
        {
            _windowPreviewRebuildScheduled = false;
            _logger.Warning(ex, "Falha nÃ£o tratada em ScheduleWindowPreviewRebuild.");
        }
    }

    private void RebuildSimulationOverlays()
    {
        using var logicalDepth = new MonitorPreviewDisplay.PreviewLogicalScope(nameof(RebuildSimulationOverlays), _logger);
        if (!logicalDepth.Entered)
        {
            _logger.Error("RebuildSimulationOverlays: preview logical depth limit reached; aborting rebuild");
            return;
        }

        using var depth = new MonitorPreviewDisplay.PreviewCallScope(nameof(RebuildSimulationOverlays), _logger);
        if (!depth.Entered)
        {
            _logger.Error("RebuildSimulationOverlays: depth limit reached; aborting rebuild");
            return;
        }

        var currentDepth = Interlocked.Increment(ref _simOverlaysDepth);
        if (currentDepth > 8)
        {
            _logger.Error("sim_overlays_depth_limit_reached depth={Depth} limit={Limit}", currentDepth, 8);
            Interlocked.Decrement(ref _simOverlaysDepth);
            return;
        }

        if (_inRebuildSimulationOverlays)
        {
            _logger.Debug("RebuildSimulationOverlays: recursion blocked");
            Interlocked.Decrement(ref _simOverlaysDepth);
            return;
        }

        _inRebuildSimulationOverlays = true;
        try
        {
            if (monitorPreviewDisplay is null)
            {
                _logger.Debug("RebuildSimulationOverlays: skip missing preview display");
                return;
            }

            var monitor = GetSelectedMonitor();
            if (monitor is null)
            {
                monitorPreviewDisplay.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
                _logger.Debug("RebuildSimulationOverlays: exit no monitor selected");
                return;
            }

            var monitorId = MonitorIdentifier.Create(monitor);
            if (string.IsNullOrWhiteSpace(monitorId))
            {
                monitorPreviewDisplay.SetSimulationRects(Array.Empty<MonitorPreviewDisplay.SimRect>());
                _logger.Debug(
                    "RebuildSimulationOverlays: exit invalid monitor identifier monitor={MonitorName}",
                    monitor.Name ?? string.Empty);
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
                if (relativeBounds.Width < MinimumOverlayDimension || relativeBounds.Height < MinimumOverlayDimension)
                {
                    _logger.Debug(
                        "RebuildSimulationOverlays: ignored_zero_bound_overlay bounds={Bounds} appId={AppId} monitorId={MonitorId}",
                        relativeBounds,
                        app.Id ?? string.Empty,
                        monitorId);
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
            _logger.Debug(
                "RebuildSimulationOverlays: exit monitorId={MonitorId} overlayCandidates={OverlayCandidates} overlaysRendered={OverlaysRendered} currentAppId={CurrentAppId}",
                monitorId,
                overlayApps.Count,
                overlays.Count,
                current.Id ?? string.Empty);
        }
        finally
        {
            _inRebuildSimulationOverlays = false;
            var depthAfter = Interlocked.Decrement(ref _simOverlaysDepth);
            if (depthAfter < 0)
            {
                Interlocked.Exchange(ref _simOverlaysDepth, 0);
            }
        }
    }

    private void CacheOverlaySnapshot(WindowPreviewSnapshot snapshot)
    {
        _lastOverlayBounds = snapshot.Bounds;
        _lastOverlayMonitorId = snapshot.MonitorId;
        _lastOverlayFullScreen = snapshot.IsFullScreen;
        _hasCachedOverlayBounds = true;
    }

    private bool ShouldApplyOverlay(WindowPreviewSnapshot snapshot)
    {
        if (!_hasCachedOverlayBounds)
        {
            return true;
        }

        if (!string.Equals(snapshot.MonitorId, _lastOverlayMonitorId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (snapshot.IsFullScreen != _lastOverlayFullScreen)
        {
            return true;
        }

        return !AreBoundsClose(snapshot.Bounds, _lastOverlayBounds, 1);
    }

    private static bool AreBoundsClose(Drawing.Rectangle current, Drawing.Rectangle previous, int tolerance)
    {
        return Math.Abs(current.X - previous.X) <= tolerance &&
               Math.Abs(current.Y - previous.Y) <= tolerance &&
               Math.Abs(current.Width - previous.Width) <= tolerance &&
               Math.Abs(current.Height - previous.Height) <= tolerance;
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

    private static Drawing.Rectangle CalculateMonitorRelativeBounds(WindowConfig window, MonitorInfo monitor)
    {
        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width;
        var monitorHeight = monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height;

        monitorWidth = Math.Max(1, monitorWidth);
        monitorHeight = Math.Max(1, monitorHeight);

        if (window.FullScreen)
        {
            return new Drawing.Rectangle(0, 0, monitorWidth, monitorHeight);
        }

        var width = window.Width ?? monitorWidth;
        var height = window.Height ?? monitorHeight;
        width = Math.Clamp(width, 1, monitorWidth);
        height = Math.Clamp(height, 1, monitorHeight);

        var x = window.X ?? 0;
        var y = window.Y ?? 0;
        x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

        var relative = new Drawing.Rectangle(x, y, width, height);

        var bounds = monitor.Bounds;
        var workArea = monitor.WorkArea;
        if (bounds.Width > 0 && bounds.Height > 0 && workArea.Width > 0 && workArea.Height > 0)
        {
            var absolute = new Drawing.Rectangle(bounds.Left + relative.X, bounds.Top + relative.Y, relative.Width, relative.Height);
            var clamped = DisplayUtils.ClampToWorkArea(absolute, workArea);
            relative = new Drawing.Rectangle(
                clamped.Left - bounds.Left,
                clamped.Top - bounds.Top,
                clamped.Width,
                clamped.Height);
        }

        return relative;
    }

    private static Drawing.Color ResolveSimulationColor(string? id)
    {
        if (SimulationPalette.Length == 0)
        {
            return Drawing.Color.DodgerBlue;
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

    private void UpdatePreviewVisibility()
    {
        var preview = monitorPreviewDisplay;
        if (preview is null || preview.IsDisposed)
        {
            return;
        }

        var previewTabSelected = tabEditor is { SelectedTab: { } selectedTab }
            && tpJanela is not null
            && ReferenceEquals(selectedTab, tpJanela)
            && tabEditor.TabPages.Contains(tpJanela);

        preview.SetPreviewVisibility(previewTabSelected);
    }

    private static WindowConfig ClampWindowBounds(WindowConfig window, MonitorInfo monitor)
    {
        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width;
        var monitorHeight = monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height;

        monitorWidth = Math.Max(1, monitorWidth);
        monitorHeight = Math.Max(1, monitorHeight);

        var width = window.Width;
        var height = window.Height;
        var x = window.X;
        var y = window.Y;

        if (width is int w)
        {
            width = Math.Clamp(w, 1, monitorWidth);
        }

        if (height is int h)
        {
            height = Math.Clamp(h, 1, monitorHeight);
        }

        if (x is int posX && width is int wValue)
        {
            var maxX = Math.Max(0, monitorWidth - wValue);
            x = Math.Clamp(posX, 0, maxX);
        }

        if (y is int posY && height is int hValue)
        {
            var maxY = Math.Max(0, monitorHeight - hValue);
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

    private static Drawing.Rectangle CalculateRelativeRectangle(WindowPlacementHelper.ZoneRect zone, MonitorInfo monitor)
    {
        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
        var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

        var width = Math.Max(1, (int)Math.Round(monitorWidth * (zone.WidthPercentage / 100d), MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(monitorHeight * (zone.HeightPercentage / 100d), MidpointRounding.AwayFromZero));
        var x = (int)Math.Round(monitorWidth * (zone.LeftPercentage / 100d), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(monitorHeight * (zone.TopPercentage / 100d), MidpointRounding.AwayFromZero);

        x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

        return new Drawing.Rectangle(x, y, width, height);
    }

    private void ApplyBoundsToInputs(Drawing.Rectangle bounds)
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
            WinForms.MessageBox.Show(
                this,
                "O teste de posicionamento estÃ¡ disponÃ­vel apenas no Windows.",
                "Teste de janela",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return;
        }

        if (rbExe?.Checked == true && string.IsNullOrWhiteSpace(txtExecutavel?.Text))
        {
            WinForms.MessageBox.Show(
                this,
                "Informe o caminho do executÃ¡vel antes de iniciar o teste.",
                "Teste de janela",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Warning);
            return;
        }

        var monitor = GetSelectedMonitor();
        if (monitor is null)
        {
            WinForms.MessageBox.Show(
                this,
                "Selecione um monitor para testar a posiÃ§Ã£o.",
                "Teste de janela",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
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
            _logger.Error("Falha ao testar a posiÃ§Ã£o em modo simulado.", ex);
            WinForms.MessageBox.Show(
                this,
                $"NÃ£o foi possÃ­vel testar a posiÃ§Ã£o: {ex.Message}",
                "Teste de janela",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
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

            try
            {
                await SuspendPreviewCaptureAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Falha ao suspender prÃ©-visualizaÃ§Ã£o antes de testar app real.");
            }
            OnBeforeMoveWindowUI();

            var app = ConstruirPrograma();
            await _appTestRunner.RunTestAsync(app, this).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Falha ao executar o aplicativo real durante o teste.", ex);
            WinForms.MessageBox.Show(
                this,
                $"NÃ£o foi possÃ­vel executar o aplicativo real: {ex.Message}",
                "Teste de aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
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
        out Drawing.Rectangle bounds,
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
            WinForms.MessageBox.Show(
                this,
                "Selecione um monitor para testar a posiÃ§Ã£o.",
                messageTitle,
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
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
            WinForms.MessageBox.Show(
                this,
                "Informe um executÃ¡vel vÃ¡lido antes de executar o teste.",
                messageTitle,
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return false;
        }

        if (requireExecutable && !string.IsNullOrWhiteSpace(executablePath) && !File.Exists(executablePath))
        {
            WinForms.MessageBox.Show(
                this,
                "ExecutÃ¡vel nÃ£o encontrado. Ajuste o caminho antes de executar o teste.",
                messageTitle,
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
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
            throw new InvalidOperationException("NÃ£o foi possÃ­vel iniciar o aplicativo selecionado.");
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
            throw new InvalidOperationException("NÃ£o foi possÃ­vel iniciar a janela de teste.");
        }

        try
        {
            var handle = await WindowWaiter.WaitForMainWindowAsync(process, WindowTestTimeout, CancellationToken.None).ConfigureAwait(true);
            var bounds = WindowPlacementHelper.ResolveBounds(window, monitor);
            try
            {
                await SuspendPreviewCaptureAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Falha ao suspender prÃ©-visualizaÃ§Ã£o antes do movimento da janela de teste.");
            }
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

    private async void AppRunnerOnBeforeMoveWindow(object? sender, EventArgs e)
    {
        try
        {
            await SuspendPreviewCaptureAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Falha ao suspender captura de preview antes de mover janela.");
        }
    }

    private void AppRunnerOnAfterMoveWindow(object? sender, EventArgs e)
    {
        SchedulePreviewResume();
    }

    private async Task SuspendPreviewCaptureAsync()
    {
        if (monitorPreviewDisplay is null)
        {
            return;
        }

        await monitorPreviewDisplay.SuspendCaptureAsync().ConfigureAwait(true);
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
            // Ignorar quando o formulÃ¡rio estiver sendo finalizado.
        }
        catch (InvalidOperationException)
        {
            // Ignorar quando o handle nÃ£o estiver disponÃ­vel.
        }
    }

    private async void ResumePreviewCaptureWithDelay()
    {
        try
        {
            try
            {
                await Task.Delay(PreviewResumeDelay).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            await ResumePreviewCaptureAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Falha ao retomar captura de preview com atraso.");
        }
    }

    private async Task ResumePreviewCaptureAsync()
    {
        if (monitorPreviewDisplay is null)
        {
            return;
        }

        await monitorPreviewDisplay.ResumeCaptureAsync().ConfigureAwait(true);
    }

    private void AppEditorForm_Disposed(object? sender, EventArgs e)
    {
        StopCycleSimulation();
        DisposeSimRectDisplays();
        _appRunner.BeforeMoveWindow -= AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow -= AppRunnerOnAfterMoveWindow;
        CancelHoverThrottleTimer();
        _logScope.Dispose();
    }

    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        if (!ValidarCampos())
        {
            DialogResult = WinForms.DialogResult.None;
            return;
        }

        Resultado = ConstruirPrograma();
        CommitProfileMetadata();
        DialogResult = WinForms.DialogResult.OK;
        Close();
    }

    private ProgramaConfig ConstruirPrograma()
    {
        var id = txtId.Text.Trim();
        var executavel = txtExecutavel.Text.Trim();
        var argumentos = string.IsNullOrWhiteSpace(txtArgumentos.Text) ? null : txtArgumentos.Text.Trim();

        var janela = chkJanelaTelaCheia.Checked
            ? new WindowConfig
            {
                FullScreen = true,
                Title = txtWindowTitle.Text.Trim(),
                AlwaysOnTop = chkAlwaysOnTop.Checked,
            }
            : new WindowConfig
            {
                FullScreen = false,
                X = (int)nudJanelaX.Value,
                Y = (int)nudJanelaY.Value,
                Width = (int)nudJanelaLargura.Value,
                Height = (int)nudJanelaAltura.Value,
                Title = txtWindowTitle.Text.Trim(),
                AlwaysOnTop = chkAlwaysOnTop.Checked,
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
            Name = string.IsNullOrWhiteSpace(txtNomeAmigavel.Text) ? null : txtNomeAmigavel.Text.Trim(),
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
            Watchdog = new WatchdogSettings
            {
                Enabled = chkWatchdogEnabled.Checked,
                RestartGracePeriodSeconds = (int)nudWatchdogGrace.Value,
                HealthCheck = BuildHealthCheckConfig(),
            },
            EnvironmentVariables = ParseEnvironmentVariables(),
        };
    }

    private IReadOnlyDictionary<string, string> ParseEnvironmentVariables()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(txtEnvVars.Text)) return dict;

        foreach (var line in txtEnvVars.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0) continue;
            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
            {
                dict[key] = value;
            }
        }

        return dict;
    }

    private HealthCheckConfig? BuildHealthCheckConfig()
    {
        var type = (HealthCheckKind)cmbHealthCheckType.SelectedIndex;
        if (type == HealthCheckKind.None)
            return null;

        return new HealthCheckConfig
        {
            Type = type,
            Url = string.IsNullOrWhiteSpace(txtHealthCheckUrl.Text) ? null : txtHealthCheckUrl.Text.Trim(),
            DomSelector = string.IsNullOrWhiteSpace(txtHealthCheckDomSelector.Text) ? null : txtHealthCheckDomSelector.Text.Trim(),
            ContainsText = string.IsNullOrWhiteSpace(txtHealthCheckContainsText.Text) ? null : txtHealthCheckContainsText.Text.Trim(),
            IntervalSeconds = (int)nudHealthCheckInterval.Value,
            TimeoutSeconds = (int)nudHealthCheckTimeout.Value,
        };
    }

    private void UpdateHealthCheckFieldsVisibility()
    {
        var type = (HealthCheckKind)cmbHealthCheckType.SelectedIndex;
        var showPing = type == HealthCheckKind.Ping || type == HealthCheckKind.Dom;
        var showDom = type == HealthCheckKind.Dom;

        txtHealthCheckUrl.Visible = showPing;
        txtHealthCheckUrl.Parent!.GetContainerControl()?.ToString(); // force layout
        nudHealthCheckInterval.Visible = showPing;
        nudHealthCheckTimeout.Visible = showPing;

        txtHealthCheckDomSelector.Visible = showDom;
        txtHealthCheckContainsText.Visible = showDom;

        // Also toggle labels
        foreach (Control ctrl in txtHealthCheckUrl.Parent!.Controls)
        {
            if (ctrl is Label lbl)
            {
                if (lbl.Name == "lblHealthCheckUrl" || lbl.Name == "lblHealthCheckInterval" || lbl.Name == "lblHealthCheckTimeout")
                    lbl.Visible = showPing;
                if (lbl.Name == "lblHealthCheckDomSelector" || lbl.Name == "lblHealthCheckContainsText")
                    lbl.Visible = showDom;
            }
        }
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

    private void SetCycleCurrentCellSafe(DataGridViewCell? cell)
    {
        if (dgvCycle is null)
        {
            return;
        }

        if (cell is null || cell.DataGridView != dgvCycle)
        {
            return;
        }

        if (_settingCycleCurrentCell)
        {
            return;
        }

        try
        {
            _settingCycleCurrentCell = true;
            dgvCycle.CurrentCell = cell;
        }
        finally
        {
            _settingCycleCurrentCell = false;
        }
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
            SetCycleCurrentCellSafe(dgvCycle.Rows[index].Cells[0]);
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

        var validarExecutavel = rbExe?.Checked ?? true;
        if (validarExecutavel)
        {
            if (string.IsNullOrWhiteSpace(txtExecutavel.Text))
            {
                errorProvider.SetError(txtExecutavel, "Informe o executÃ¡vel." );
                valido = false;
            }
            else if (!File.Exists(txtExecutavel.Text))
            {
                errorProvider.SetError(txtExecutavel, "ExecutÃ¡vel nÃ£o encontrado.");
                valido = false;
            }
            else
            {
                errorProvider.SetError(txtExecutavel, string.Empty);
            }
        }
        else
        {
            errorProvider.SetError(txtExecutavel, string.Empty);

            // ValidaÃ§Ã£o modo navegador
            if (_sites.Count == 0)
            {
                errorProvider.SetError(sitesEditorControl, "Adicione pelo menos um site.");
                valido = false;
            }
            else
            {
                errorProvider.SetError(sitesEditorControl, string.Empty);
            }

            if (cmbBrowserEngine.SelectedIndex < 0)
            {
                errorProvider.SetError(cmbBrowserEngine, "Selecione um motor de navegador.");
                valido = false;
            }
            else
            {
                errorProvider.SetError(cmbBrowserEngine, string.Empty);
            }
        }

        return valido;
    }

    private void ValidarCampoId()
    {
        if (string.IsNullOrWhiteSpace(txtId.Text))
        {
            errorProvider.SetError(txtId, "Informe um identificador.");
        }
        else
        {
            errorProvider.SetError(txtId, string.Empty);
        }
    }

    private void btnCancelar_Click(object? sender, EventArgs e)
    {
        DialogResult = WinForms.DialogResult.Cancel;
        Close();
    }

    protected override bool ProcessCmdKey(ref WinForms.Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                btnSalvar_Click(this, EventArgs.Empty);
                return true;
            case Keys.Control | Keys.D1:
                tabEditor.SelectedIndex = 0;
                return true;
            case Keys.Control | Keys.D2:
                if (tabEditor.TabCount > 1) tabEditor.SelectedIndex = 1;
                return true;
            case Keys.Control | Keys.D3:
                if (tabEditor.TabCount > 2) tabEditor.SelectedIndex = 2;
                return true;
            case Keys.Control | Keys.D4:
                if (tabEditor.TabCount > 3) tabEditor.SelectedIndex = 3;
                return true;
            case Keys.Control | Keys.D5:
                if (tabEditor.TabCount > 4) tabEditor.SelectedIndex = 4;
                return true;
            case Keys.Control | Keys.D6:
                if (tabEditor.TabCount > 5) tabEditor.SelectedIndex = 5;
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override async void OnFormClosing(WinForms.FormClosingEventArgs e)
    {
        if (DialogResult != WinForms.DialogResult.OK && _isDirty)
        {
            var result = MessageBox.Show(
                this,
                "Existem alteraÃ§Ãµes nÃ£o salvas. Deseja descartar?",
                "ConfirmaÃ§Ã£o",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != WinForms.DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        StopCycleSimulation();
        base.OnFormClosing(e);

        if (monitorPreviewDisplay is { } preview)
        {
            try
            {
                await preview.StopPreviewAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Falha ao encerrar prÃ©-visualizaÃ§Ã£o ao fechar o editor.");
            }
        }
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
                    // Ignored: falha ao limpar a seleÃ§Ã£o nÃ£o deve impedir a seleÃ§Ã£o personalizada.
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
            WinForms.MessageBox.Show(
                this,
                "Selecione um executÃ¡vel vÃ¡lido para abrir.",
                "Abrir aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Warning);
            return;
        }

        try
        {
            await Task.Run(() => LaunchExecutable(e.ExecutablePath, e.Arguments)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(
                this,
                $"NÃ£o foi possÃ­vel abrir o aplicativo selecionado: {ex.Message}",
                "Abrir aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private async Task AppsTab_TestRequestedAsync(object? sender, AppExecutionRequestEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            WinForms.MessageBox.Show(
                this,
                "O teste de posicionamento estÃ¡ disponÃ­vel apenas no Windows.",
                "Teste de aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
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
            WinForms.MessageBox.Show(
                this,
                $"NÃ£o foi possÃ­vel testar a posiÃ§Ã£o: {ex.Message}",
                "Teste de aplicativo",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
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
        public SimRectDisplay(AppCycleSimulator.SimRect rect, WinForms.Panel panel, WinForms.Label label)
        {
            Rect = rect;
            Panel = panel;
            Label = label;
            NormalFont = label.Font;
            BoldFont = new Drawing.Font(label.Font, FontStyle.Bold);
            LastResult = SimRectStatus.None;
        }

        public AppCycleSimulator.SimRect Rect { get; }

        public WinForms.Panel Panel { get; }

        public WinForms.Label Label { get; }

        public Drawing.Font NormalFont { get; }

        public Drawing.Font BoldFont { get; }

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
            => new(null, null, "Nenhum monitor disponÃ­vel");

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
