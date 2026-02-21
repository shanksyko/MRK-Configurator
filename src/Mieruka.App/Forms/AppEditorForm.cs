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

        var editorTabs = tabEditor ?? throw new InvalidOperationException("O TabControl do editor não foi carregado.");
        var appsTabPage = tabAplicativos ?? throw new InvalidOperationException("A aba de aplicativos não foi configurada.");
        var sitesTabPage = tabSites ?? throw new InvalidOperationException("A aba de sites não foi configurada.");
        _ = pnlBrowserPanel ?? throw new InvalidOperationException("O painel de opções de navegador não foi configurado.");
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
        _ = btnBrowseExe ?? throw new InvalidOperationException("O botão de procurar executáveis não foi carregado.");
        _ = cmbBrowserEngine ?? throw new InvalidOperationException("O seletor de motor de navegador não foi carregado.");
        _ = lblBrowserDetected ?? throw new InvalidOperationException("O rótulo de navegadores detectados não foi carregado.");

        _ = tlpMonitorPreview ?? throw new InvalidOperationException("O painel de pré-visualização não foi configurado.");
        var previewControl = monitorPreviewDisplay ?? throw new InvalidOperationException("O controle de pré-visualização do monitor não foi configurado.");
        previewControl.IsCoordinateAnalysisMode = true;
        previewControl.EditSessionId = _editSessionId;
        previewControl.PreviewStarted += MonitorPreviewDisplayOnPreviewStarted;
        previewControl.PreviewStopped += MonitorPreviewDisplayOnPreviewStopped;
        _ = lblMonitorCoordinates ?? throw new InvalidOperationException("O rótulo de coordenadas do monitor não foi configurado.");
        var janelaTab = tpJanela ?? throw new InvalidOperationException("A aba de janela não foi configurada.");

        // Health Check combo setup
        cmbHealthCheckType.SelectedIndex = 0;
        cmbHealthCheckType.SelectedIndexChanged += (_, _) => UpdateHealthCheckFieldsVisibility();
        UpdateHealthCheckFieldsVisibility();

        _tabAplicativosIndex = editorTabs.TabPages.IndexOf(appsTabPage);
        if (_tabAplicativosIndex < 0)
        {
            throw new InvalidOperationException("A aba de aplicativos não foi adicionada ao controle de abas.");
        }

        _tabSitesIndex = editorTabs.TabPages.IndexOf(sitesTabPage);
        if (_tabSitesIndex < 0)
        {
            throw new InvalidOperationException("A aba de sites não foi adicionada ao controle de abas.");
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
        Shown += (_, __) =>
        {
            // Apps list is loaded lazily when the user switches to the "Aplicativos" tab.
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
            _ = UpdateMonitorPreviewSafelyAsync();
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

        // Avançado
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
}

