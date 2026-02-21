#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Inventory;
using Mieruka.App.Forms.Security;
using Mieruka.App.Services.Ui;
using Mieruka.App.Ui;
using Mieruka.App.Services;
using Mieruka.Automation.Execution;
using Mieruka.Core.Config;
using Mieruka.Core.Data.Services;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Core.Security;
using Mieruka.Core.Security.Data;
using Mieruka.Core.Security.Services;
using Mieruka.Core.Services;
using Mieruka.App.Ui.PreviewBindings;
using Serilog;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Forms;

public partial class MainForm : WinForms.Form, IMonitorSelectionProvider
{
    private readonly BindingList<ProgramaConfig> _programas = new();
    private readonly ITelemetry _telemetry = new UiTelemetry();
    private readonly Orchestrator _orchestrator;
    private readonly IAppRunner _appRunner;
    private readonly AppTestRunner _appTestRunner;
    private bool _busy;
    private readonly List<MonitorInfo> _monitorSnapshot = new();
    private readonly List<MonitorCardContext> _monitorCardOrder = new();
    private readonly IMonitorService _monitorService = new MonitorService();
    private readonly List<MonitorPreviewHost> _monitorHosts = new();
    private readonly Dictionary<string, MonitorCardContext> _monitorCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _manuallyStoppedMonitors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _requestedPreviews = new(StringComparer.OrdinalIgnoreCase);
    private WinForms.Control? pictureBoxPreview;
    private IDisplayService? _displayService;
    private bool _previewsRequested;
    private readonly ProfileStore _profileStore = new();
    private readonly JsonStore<PreviewGraphicsOptions> _graphicsOptionsStore;
    private PreviewGraphicsOptions _graphicsOptions;
    private WinForms.ToolStrip? _toolStrip;
    private WinForms.ToolStripDropDownButton? _graphicsDropDown;
    private WinForms.ToolStripMenuItem? _graphicsAutoItem;
    private WinForms.ToolStripMenuItem? _graphicsGpuItem;
    private WinForms.ToolStripMenuItem? _graphicsGdiItem;
    private ProfileExecutor? _profileExecutor;
    private CancellationTokenSource? _profileExecutionCts;
    private Task? _profileExecutionTask;
    private bool _profileRunning;
    private ProfileConfig? _currentProfile;
    private static readonly Regex ProfileIdSanitizer = new("[^A-Za-z0-9_-]+", RegexOptions.Compiled);
    private const string DefaultProfileId = "workspace";
    private static readonly TimeSpan MonitorPreviewResumeDelay = TimeSpan.FromMilliseconds(150);
    private System.Windows.Forms.Timer? _resizeDebounce;
    private System.Windows.Forms.Timer? _topologyDebounce;

    private Control PreviewBox => pictureBoxPreview ?? throw new InvalidOperationException("Nenhum controle de preview foi inicializado.");

    public string? SelectedMonitorId { get; private set; }

    IReadOnlyList<MonitorInfo> IMonitorSelectionProvider.GetAvailableMonitors()
    {
        return _monitorSnapshot.Count > 0
            ? _monitorSnapshot.ToList()
            : CaptureMonitorSnapshot();
    }

    public event EventHandler<string>? MonitorSelected;

    public MainForm(PreviewGraphicsOptions? preloadedOptions = null)
    {
        _graphicsOptionsStore = CreateGraphicsOptionsStore();
        _graphicsOptions = preloadedOptions?.Normalize() ?? LoadGraphicsOptions();

        InitializeComponent();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        this.DoubleBuffered = true;
        EnsureToolbarWithOptionsButton();
        ToolTipTamer.Tame(this, components);

        _appRunner = new AppRunner();
        _appRunner.BeforeMoveWindow += AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow += AppRunnerOnAfterMoveWindow;
        _appTestRunner = new AppTestRunner(this, _appRunner);

        UpdateStatusText("Pronto");

        var grid = dgvProgramas ?? throw new InvalidOperationException("O DataGridView de programas não foi criado pelo designer.");
        var source = bsProgramas ?? throw new InvalidOperationException("A BindingSource de programas não foi inicializada.");
        _ = errorProvider ?? throw new InvalidOperationException("O ErrorProvider não foi criado.");

        source.DataSource = _programas;
        grid.AutoGenerateColumns = false;
        grid.DataSource = source;

        grid.SelectionChanged += (_, _) => UpdateButtonStates();
        grid.CellDoubleClick += (_, _) => btnEditar_Click(this, EventArgs.Empty);
        grid.KeyDown += dgvProgramas_KeyDown;

        _orchestrator = CreateOrchestrator();
        _orchestrator.StateChanged += Orchestrator_StateChanged;

        InitializeMonitorInfrastructure();
        Shown += MainForm_Shown;
        Resize += MainForm_Resize;
        FormClosing += MainForm_FormClosing;
        Disposed += MainForm_Disposed;

        LoadInitialData();
        LoadProfileFromStore();
        UpdateButtonStates();
    }

    private void EnsureToolbarWithOptionsButton()
    {
        _toolStrip = Controls.OfType<WinForms.ToolStrip>().FirstOrDefault();
        if (_toolStrip == null)
        {
            _toolStrip = new WinForms.ToolStrip
            {
                GripStyle = WinForms.ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
                Dock = WinForms.DockStyle.Top
            };
            Controls.Add(_toolStrip);
            _toolStrip.BringToFront();
        }

        const string dropDownName = "previewGraphicsDropDown";
        _graphicsDropDown = _toolStrip.Items
            .OfType<WinForms.ToolStripDropDownButton>()
            .FirstOrDefault(b => string.Equals(b.Name, dropDownName, StringComparison.Ordinal));

        if (_graphicsDropDown is null)
        {
            _graphicsDropDown = new WinForms.ToolStripDropDownButton
            {
                Name = dropDownName,
                Text = "Prévia",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Selecionar modo de captura do preview"
            };
            _toolStrip.Items.Add(_graphicsDropDown);
        }
        else
        {
            _graphicsDropDown.DropDownItems.Clear();
        }

        _graphicsAutoItem = CreateGraphicsMenuItem("Auto (GDI)", PreviewGraphicsMode.Auto, "Usa GDI por padrão, sem competir pela GPU");
        _graphicsGpuItem = CreateGraphicsMenuItem("GPU", PreviewGraphicsMode.Gpu, "Usa GPU para captura (pode conflitar com outros apps)");
        _graphicsGdiItem = CreateGraphicsMenuItem("GDI", PreviewGraphicsMode.Gdi, "Força o modo GDI para o preview");

        _graphicsDropDown.DropDownItems.AddRange(new WinForms.ToolStripItem[]
        {
            _graphicsAutoItem,
            _graphicsGpuItem,
            _graphicsGdiItem
        });

        UpdateGraphicsMenuSelection(_graphicsOptions.Mode);

        const string openLogsButtonName = "openLogsButton";
        var openLogsButton = _toolStrip.Items
            .OfType<WinForms.ToolStripButton>()
            .FirstOrDefault(b => string.Equals(b.Name, openLogsButtonName, StringComparison.Ordinal));

        if (openLogsButton is null)
        {
            openLogsButton = new WinForms.ToolStripButton
            {
                Name = openLogsButtonName,
                Text = "Abrir logs",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Abrir pasta de logs de diagnóstico"
            };
            openLogsButton.Click += (_, _) => OpenLogsFolder();
            _toolStrip.Items.Add(openLogsButton);
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            var logsDirectory = Path.Combine(AppContext.BaseDirectory ?? string.Empty, "logs");
            Directory.CreateDirectory(logsDirectory);

            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = logsDirectory,
                UseShellExecute = true,
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open logs folder");
        }
    }

    private WinForms.ToolStripMenuItem CreateGraphicsMenuItem(string text, PreviewGraphicsMode mode, string toolTip)
    {
        var item = new WinForms.ToolStripMenuItem
        {
            Text = text,
            ToolTipText = toolTip,
            Tag = mode,
            CheckOnClick = false,
        };
        item.Click += OnGraphicsModeMenuItemClick;
        return item;
    }

    private async void OnGraphicsModeMenuItemClick(object? sender, EventArgs e)
    {
        if (sender is not WinForms.ToolStripMenuItem item || item.Tag is not PreviewGraphicsMode mode)
        {
            return;
        }

        try
        {
            await SetGraphicsModeAsync(mode).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao alterar modo gráfico.", ex);
        }
    }

    private async Task ApplyGraphicsOptionsAsync()
    {
        UpdateGraphicsMenuSelection(_graphicsOptions.Mode);

        var safeMode = _graphicsOptions.Mode == PreviewGraphicsMode.Gdi;
        var preferGpu = ShouldPreferGpu();

        foreach (var context in _monitorCardOrder)
        {
            context.Host.PreviewSafeModeEnabled = safeMode;

            var wasRunning = context.Host.Capture is not null;
            var shouldRestart = wasRunning
                || (_previewsRequested
                    && _requestedPreviews.Contains(context.MonitorId)
                    && !_manuallyStoppedMonitors.Contains(context.MonitorId)
                    && WindowState != WinForms.FormWindowState.Minimized);

            if (!shouldRestart)
            {
                continue;
            }

            await context.Host.StopSafeAsync().ConfigureAwait(true);

            if (_manuallyStoppedMonitors.Contains(context.MonitorId)
                || WindowState == WinForms.FormWindowState.Minimized)
            {
                continue;
            }

            try
            {
                var started = await context.Host.StartSafeAsync(preferGpu).ConfigureAwait(true);
                if (!started)
                {
                    _telemetry.Warn($"Pré-visualização do monitor '{context.MonitorId}' não pôde ser iniciada.");
                }
            }
            catch (Exception ex)
            {
                _telemetry.Warn($"Falha ao reiniciar preview do monitor '{context.MonitorId}'.", ex);
            }
        }

        SaveGraphicsOptions(_graphicsOptions);
    }

    private void UpdateGraphicsMenuSelection(PreviewGraphicsMode mode)
    {
        if (_graphicsAutoItem is not null)
        {
            _graphicsAutoItem.Checked = mode == PreviewGraphicsMode.Auto;
        }

        if (_graphicsGpuItem is not null)
        {
            _graphicsGpuItem.Checked = mode == PreviewGraphicsMode.Gpu;
        }

        if (_graphicsGdiItem is not null)
        {
            _graphicsGdiItem.Checked = mode == PreviewGraphicsMode.Gdi;
        }

        if (_graphicsDropDown is not null)
        {
            var label = mode switch
            {
                PreviewGraphicsMode.Auto => "Auto",
                PreviewGraphicsMode.Gpu => "GPU",
                PreviewGraphicsMode.Gdi => "GDI",
                _ => "Auto"
            };
            _graphicsDropDown.Text = $"Prévia ({label})";
        }
    }

    private async Task SetGraphicsModeAsync(PreviewGraphicsMode mode)
    {
        if (_graphicsOptions.Mode == mode)
        {
            UpdateGraphicsMenuSelection(mode);
            return;
        }

        _graphicsOptions = _graphicsOptions with { Mode = mode };
        Log.Information("UserSetHardwareAcceleration: {Mode}", mode);
        await ApplyGraphicsOptionsAsync().ConfigureAwait(true);
    }

    private bool ShouldPreferGpu()
        => _graphicsOptions.Mode == PreviewGraphicsMode.Gpu;

    private static JsonStore<PreviewGraphicsOptions> CreateGraphicsOptionsStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        var directory = Path.Combine(localAppData, "Mieruka", "Configurator");
        return new JsonStore<PreviewGraphicsOptions>(Path.Combine(directory, "preview-options.json"));
    }

    private PreviewGraphicsOptions LoadGraphicsOptions()
    {
        try
        {
            // Use synchronous file I/O during construction to avoid
            // blocking the thread pool via GetAwaiter().GetResult().
            var path = _graphicsOptionsStore.FilePath;
            if (!File.Exists(path))
            {
                return new PreviewGraphicsOptions();
            }

            var json = File.ReadAllText(path);
            var options = System.Text.Json.JsonSerializer.Deserialize<PreviewGraphicsOptions>(json);
            return options?.Normalize() ?? new PreviewGraphicsOptions();
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao carregar preferências de preview.", ex);
            return new PreviewGraphicsOptions();
        }
    }

    private void SaveGraphicsOptions(PreviewGraphicsOptions options)
    {
        _ = SaveGraphicsOptionsAsync(options);
    }

    private async Task SaveGraphicsOptionsAsync(PreviewGraphicsOptions options)
    {
        try
        {
            await _graphicsOptionsStore.SaveAsync(options.Normalize()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao salvar preferências de preview.", ex);
        }
    }

    private void LoadInitialData()
    {
        if (_programas.Count > 0)
        {
            return;
        }

        _programas.Add(new ProgramaConfig
        {
            Id = "app_principal",
            ExecutablePath = @"C:\\Program Files\\Mieruka\\MierukaPlayer.exe",
            AutoStart = true,
            TargetMonitorStableId = string.Empty,
        });
    }

    private Orchestrator CreateOrchestrator()
    {
        var component = new NoOpComponent();
        return new Orchestrator(component, component, component, component, _telemetry);
    }

    private void InitializeMonitorInfrastructure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _displayService = new DisplayService(_telemetry);
            _displayService.TopologyChanged += DisplayService_TopologyChanged;
        }
        catch (Exception ex)
        {
            _telemetry.Error("Não foi possível inicializar o serviço de monitores.", ex);
        }
    }

    private void DisplayService_TopologyChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                _topologyDebounce?.Stop();
                _topologyDebounce?.Dispose();
                _topologyDebounce = new System.Windows.Forms.Timer { Interval = 300 };
                _topologyDebounce.Tick += async (_, _) =>
                {
                    _topologyDebounce?.Stop();
                    await RefreshMonitorCardsAsync().ConfigureAwait(true);
                };
                _topologyDebounce.Start();
            }));
        }
        catch (ObjectDisposedException)
        {
            // Ignore when the form is closing.
        }
        catch (InvalidOperationException)
        {
            // Ignore when the handle is not available anymore.
        }
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            await RefreshMonitorCardsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao carregar monitor cards.", ex);
        }
    }

    private async void MainForm_Resize(object? sender, EventArgs e)
    {
        try
        {
            if (WindowState == WinForms.FormWindowState.Minimized)
            {
                _resizeDebounce?.Stop();
                await PausePreviewsAsync().ConfigureAwait(true);
                return;
            }

            if (_previewsRequested)
            {
                _resizeDebounce?.Stop();
                _resizeDebounce?.Dispose();
                _resizeDebounce = new System.Windows.Forms.Timer { Interval = 200 };
                _resizeDebounce.Tick += async (_, _) =>
                {
                    _resizeDebounce?.Stop();
                    await StartAutomaticPreviewsAsync().ConfigureAwait(true);
                };
                _resizeDebounce.Start();
            }
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao reagir a redimensionamento.", ex);
        }
    }

    private void MainForm_FormClosing(object? sender, WinForms.FormClosingEventArgs e)
    {
        _resizeDebounce?.Stop();
        _resizeDebounce?.Dispose();
        _topologyDebounce?.Stop();
        _topologyDebounce?.Dispose();

        StopAutomaticPreviews(clearManualState: true);
        DisposeMonitorCards();

        if (_profileRunning)
        {
            try
            {
                _profileExecutor?.Stop();
                _profileExecutionCts?.Cancel();

                // Never block form shutdown on background profile completion.
                var executionTask = _profileExecutionTask;
                if (executionTask is not null && !executionTask.IsCompleted)
                {
                    _ = executionTask.ContinueWith(
                        t => _telemetry.Warn("Falha ao finalizar tarefa de execução de perfil durante encerramento.", t.Exception?.GetBaseException()),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                }
            }
            catch (Exception ex)
            {
                _telemetry.Warn("Falha ao interromper execução de perfil durante encerramento.", ex);
            }
        }

        _profileExecutionCts?.Dispose();
        _profileExecutionCts = null;
        _profileExecutionTask = null;

        // Unsubscribe event handlers to prevent potential leaks before disposing.
        if (_profileExecutor is not null)
        {
            _profileExecutor.AppStarted -= ProfileExecutor_AppStarted;
            _profileExecutor.AppPositioned -= ProfileExecutor_AppPositioned;
            _profileExecutor.Completed -= ProfileExecutor_Completed;
            _profileExecutor.Failed -= ProfileExecutor_Failed;
        }

        _profileExecutor?.Dispose();
        _profileExecutor = null;

        if (_displayService is not null)
        {
            _displayService.TopologyChanged -= DisplayService_TopologyChanged;
            _displayService.Dispose();
            _displayService = null;
        }

        _orchestrator.StateChanged -= Orchestrator_StateChanged;
    }

    private void tlpMonitores_SizeChanged(object? sender, EventArgs e)
    {
        UpdateMonitorColumns();
    }

    private async Task RefreshMonitorCardsAsync()
    {
        if (tlpMonitores is null)
        {
            return;
        }

        var monitors = CaptureMonitorSnapshot().ToList();
        var descriptors = EnumerateMonitorDescriptors();
        var cardSources = BuildMonitorSources(monitors, descriptors);

        monitors = new List<MonitorInfo>(cardSources.Count);
        foreach (var source in cardSources)
        {
            monitors.Add(source.Monitor);
        }

        _monitorSnapshot.Clear();
        _monitorSnapshot.AddRange(monitors);

        var expectedIds = new HashSet<string>(cardSources.Select(ResolveMonitorId), StringComparer.OrdinalIgnoreCase);
        _manuallyStoppedMonitors.RemoveWhere(id => !expectedIds.Contains(id));
        _requestedPreviews.RemoveWhere(id => !expectedIds.Contains(id));
        _previewsRequested = _requestedPreviews.Count > 0;

        var shouldRestart = _previewsRequested && WindowState != WinForms.FormWindowState.Minimized;

        DisposeMonitorCards();

        UpdateMonitorColumns();

        var actualIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var safeMode = _graphicsOptions.Mode == PreviewGraphicsMode.Gdi;
        var preferGpu = ShouldPreferGpu();

        foreach (var source in cardSources)
        {
            var monitor = source.Monitor;
            var card = LayoutHelpers.CreateMonitorCard(
                monitor,
                OnMonitorCardSelected,
                OnMonitorCardStopRequested,
                OnMonitorCardTestRequested,
                out var pictureBox);

            var monitorId = ResolveMonitorId(source);
            MonitorPreviewHost host;

            if (source.Descriptor is not null)
            {
                host = new MonitorPreviewHost(source.Descriptor, pictureBox);
                monitorId = host.MonitorId;
            }
            else
            {
                host = new MonitorPreviewHost(monitorId, pictureBox);
            }

            host.PreviewSafeModeEnabled = safeMode;
            actualIds.Add(monitorId);
            ApplyMonitorId(card, monitorId);

            var context = new MonitorCardContext(monitorId, monitor, card, host);

            pictureBoxPreview ??= pictureBox;

            pictureBox.Click += OnMonitorPreviewClicked;
            _monitorCardOrder.Add(context);
            _monitorCards[monitorId] = context;
            _monitorHosts.Add(host);

            if (shouldRestart
                && _requestedPreviews.Contains(monitorId)
                && !_manuallyStoppedMonitors.Contains(monitorId))
            {
                try
                {
                    var started = await host.StartAsync(preferGpu).ConfigureAwait(true);
                    if (!started)
                    {
                        _telemetry.Warn($"Pré-visualização do monitor '{monitorId}' não pôde ser iniciada.");
                    }
                }
                catch (Exception ex)
                {
                    _telemetry.Warn($"Falha ao iniciar preview do monitor '{monitorId}'.", ex);
                }
            }
        }

        if (SelectedMonitorId is not null && !actualIds.Contains(SelectedMonitorId))
        {
            SelectedMonitorId = null;
        }

        if (SelectedMonitorId is null && _monitorCardOrder.Count > 0)
        {
            UpdateSelectedMonitor(_monitorCardOrder[0].MonitorId, notify: false);
        }
        else
        {
            UpdateMonitorSelectionVisuals();
        }

        RelayoutMonitorCards();
    }

    private IReadOnlyList<MonitorDescriptor> EnumerateMonitorDescriptors()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<MonitorDescriptor>();
        }

        try
        {
            return _monitorService.GetAll();
        }
        catch
        {
            return Array.Empty<MonitorDescriptor>();
        }
    }

    private List<MonitorCardSource> BuildMonitorSources(
        List<MonitorInfo> monitors,
        IReadOnlyList<MonitorDescriptor> descriptors)
    {
        var result = new List<MonitorCardSource>(monitors.Count + descriptors.Count);
        var descriptorsById = new Dictionary<string, MonitorDescriptor>(StringComparer.OrdinalIgnoreCase);
        var descriptorsByDevice = new Dictionary<string, MonitorDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in descriptors)
        {
            var id = MonitorPreviewHost.CreateMonitorId(descriptor);
            if (!string.IsNullOrWhiteSpace(id))
            {
                descriptorsById[id] = descriptor;
            }

            if (!string.IsNullOrWhiteSpace(descriptor.DeviceName))
            {
                descriptorsByDevice[descriptor.DeviceName] = descriptor;
            }
        }

        var used = new HashSet<MonitorDescriptor>();

        foreach (var monitor in monitors)
        {
            var descriptor = FindDescriptorForMonitor(monitor, descriptorsById, descriptorsByDevice);
            if (descriptor is not null)
            {
                used.Add(descriptor);
            }

            result.Add(new MonitorCardSource(monitor, descriptor));
        }

        var displayIndex = monitors.Count;
        foreach (var descriptor in descriptors)
        {
            if (used.Contains(descriptor))
            {
                continue;
            }

            var fallback = CreateMonitorInfo(descriptor, displayIndex++);
            monitors.Add(fallback);
            result.Add(new MonitorCardSource(fallback, descriptor));
        }

        return result;
    }

    private static MonitorDescriptor? FindDescriptorForMonitor(
        MonitorInfo monitor,
        IDictionary<string, MonitorDescriptor> descriptorsById,
        IDictionary<string, MonitorDescriptor> descriptorsByDevice)
    {
        var monitorId = MonitorIdentifier.Create(monitor);
        if (!string.IsNullOrWhiteSpace(monitorId) && descriptorsById.TryGetValue(monitorId, out var byId))
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName) && descriptorsByDevice.TryGetValue(monitor.DeviceName, out var byDevice))
        {
            return byDevice;
        }

        var deviceId = monitor.Key.DeviceId;
        if (!string.IsNullOrWhiteSpace(deviceId) && descriptorsByDevice.TryGetValue(deviceId, out var byKey))
        {
            return byKey;
        }

        return null;
    }

    private static MonitorInfo CreateMonitorInfo(MonitorDescriptor descriptor, int displayIndex)
    {
        var deviceName = descriptor.DeviceName ?? string.Empty;
        var friendly = !string.IsNullOrWhiteSpace(descriptor.FriendlyName) ? descriptor.FriendlyName : deviceName;

        var key = new MonitorKey
        {
            DeviceId = deviceName,
            DisplayIndex = displayIndex,
            AdapterLuidHigh = (int)descriptor.AdapterLuidHi,
            AdapterLuidLow = (int)descriptor.AdapterLuidLo,
            TargetId = unchecked((int)descriptor.TargetId),
        };

        return new MonitorInfo
        {
            Key = key,
            Name = friendly,
            DeviceName = deviceName,
            Width = descriptor.Width,
            Height = descriptor.Height,
            Bounds = descriptor.Bounds,
            WorkArea = descriptor.WorkArea,
            Scale = 1.0,
            Orientation = descriptor.Orientation,
            Rotation = descriptor.Rotation,
            IsPrimary = descriptor.IsPrimary,
            StableId = MonitorIdentifier.Create(key, deviceName),
        };
    }

    private static string ResolveMonitorId(MonitorCardSource source)
    {
        if (source.Descriptor is not null)
        {
            return MonitorPreviewHost.CreateMonitorId(source.Descriptor);
        }

        return MonitorIdentifier.Create(source.Monitor);
    }

    private IReadOnlyList<MonitorInfo> CaptureMonitorSnapshot()
    {
        try
        {
            var monitors = _displayService?.Monitors();
            if (monitors is not null && monitors.Count > 0)
            {
                return monitors;
            }
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao consultar os monitores disponíveis.", ex);
        }

        return CreateFallbackMonitors();
    }

    private static IReadOnlyList<MonitorInfo> CreateFallbackMonitors()
    {
        var screens = WinForms.Screen.AllScreens;
        if (screens is null || screens.Length == 0)
        {
            return Array.Empty<MonitorInfo>();
        }

        var fallback = new List<MonitorInfo>(screens.Length);
        foreach (var screen in screens)
        {
            var index = Array.IndexOf(screens, screen);
            fallback.Add(new MonitorInfo
            {
                Key = new MonitorKey
                {
                    DisplayIndex = index,
                    DeviceId = screen.DeviceName ?? string.Empty,
                },
                Name = string.IsNullOrWhiteSpace(screen.DeviceName) ? screen.FriendlyName() : screen.DeviceName,
                DeviceName = screen.DeviceName ?? string.Empty,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                Bounds = screen.Bounds,
                WorkArea = screen.WorkingArea,
                IsPrimary = screen.Primary,
                Scale = 1.0,
                Orientation = MonitorOrientation.Unknown,
                Rotation = 0,
            });
        }

        return fallback;
    }

    private void UpdateMonitorColumns()
    {
        if (tlpMonitores is null)
        {
            return;
        }

        var availableWidth = tlpMonitores.ClientSize.Width;
        if (availableWidth <= 0)
        {
            availableWidth = grpMonitores?.ClientSize.Width ?? ClientSize.Width;
        }

        var desiredColumns = Math.Clamp(availableWidth / 320, 1, 4);

        if (desiredColumns == tlpMonitores.ColumnCount && tlpMonitores.ColumnStyles.Count == desiredColumns)
        {
            return;
        }

        tlpMonitores.SuspendLayout();
        tlpMonitores.ColumnStyles.Clear();
        tlpMonitores.ColumnCount = desiredColumns;

        var percent = 100F / desiredColumns;
        for (var i = 0; i < desiredColumns; i++)
        {
            tlpMonitores.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, percent));
        }

        tlpMonitores.ResumeLayout(true);
        RelayoutMonitorCards();
    }

    private void RelayoutMonitorCards()
    {
        if (tlpMonitores is null)
        {
            return;
        }

        tlpMonitores.SuspendLayout();
        tlpMonitores.Controls.Clear();
        tlpMonitores.RowStyles.Clear();

        var columnCount = Math.Max(1, tlpMonitores.ColumnCount);
        var row = 0;
        var column = 0;

        foreach (var context in _monitorCardOrder)
        {
            if (column >= columnCount)
            {
                column = 0;
                row++;
            }

            if (tlpMonitores.RowStyles.Count <= row)
            {
                tlpMonitores.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));
            }

            tlpMonitores.Controls.Add(context.Card, column, row);
            column++;
        }

        tlpMonitores.RowCount = Math.Max(1, row + (column > 0 ? 1 : 0));
        tlpMonitores.ResumeLayout(true);
        UpdateMonitorSelectionVisuals();
    }

    private static void ApplyMonitorId(WinForms.Control control, string monitorId)
    {
        control.Tag = monitorId;
        foreach (WinForms.Control child in control.Controls)
        {
            ApplyMonitorId(child, monitorId);
        }
    }

    private void OnMonitorCardSelected(object? sender, EventArgs e)
    {
        if (sender is not WinForms.Control control || control.Tag is not string monitorId)
        {
            return;
        }

        if (_monitorCards.ContainsKey(monitorId))
        {
            UpdateSelectedMonitor(monitorId);
        }
    }

    private async void OnMonitorPreviewClicked(object? sender, EventArgs e)
    {
        if (sender is not WinForms.Control control || control.Tag is not string monitorId)
        {
            return;
        }

        if (!_monitorCards.TryGetValue(monitorId, out var context))
        {
            return;
        }

        try
        {
            if (context.Host.IsPreviewRunning)
            {
                _requestedPreviews.Remove(monitorId);
                _manuallyStoppedMonitors.Add(monitorId);
                _previewsRequested = _requestedPreviews.Count > 0;
                context.Host.SetPreviewRequestedByUser(false);
                await context.Host.StopSafeAsync().ConfigureAwait(true);
                return;
            }

            _manuallyStoppedMonitors.Remove(monitorId);
            _requestedPreviews.Add(monitorId);
            _previewsRequested = true;
            context.Host.SetPreviewRequestedByUser(true);

            var started = await context.Host.StartSafeAsync(preferGpu: ShouldPreferGpu()).ConfigureAwait(true);
            if (!started)
            {
                _requestedPreviews.Remove(monitorId);
                _telemetry.Warn($"Pré-visualização do monitor '{monitorId}' não pôde ser iniciada.");
            }
        }
        catch (Exception ex)
        {
            _telemetry.Warn($"Falha ao alternar pré-visualização para o monitor '{monitorId}'.", ex);
        }
    }

    private async void OnMonitorCardStopRequested(object? sender, EventArgs e)
    {
        if (sender is not WinForms.Control control || control.Tag is not string monitorId)
        {
            return;
        }

        try
        {
            if (_monitorCards.TryGetValue(monitorId, out var context))
            {
                _requestedPreviews.Remove(monitorId);
                _previewsRequested = _requestedPreviews.Count > 0;
                context.Host.SetPreviewRequestedByUser(false);
                await context.Host.StopSafeAsync().ConfigureAwait(true);
                await context.CloseTestWindowAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _telemetry.Warn($"Falha ao parar preview do monitor '{monitorId}'.", ex);
        }

        _manuallyStoppedMonitors.Add(monitorId);
    }

    private async void OnMonitorCardTestRequested(object? sender, EventArgs e)
    {
        if (sender is not WinForms.Control control || control.Tag is not string monitorId)
        {
            return;
        }

        if (!_monitorCards.TryGetValue(monitorId, out var context))
        {
            return;
        }

        try
        {
            _manuallyStoppedMonitors.Remove(monitorId);
            _requestedPreviews.Add(monitorId);
            _previewsRequested = true;

            context.Host.PreviewSafeModeEnabled = _graphicsOptions.Mode == PreviewGraphicsMode.Gdi;
            if (context.Host.Capture is null)
            {
                context.Host.SetPreviewRequestedByUser(true);
                var started = await context.Host.StartSafeAsync(preferGpu: ShouldPreferGpu()).ConfigureAwait(true);
                if (!started)
                {
                    _requestedPreviews.Remove(monitorId);
                    _previewsRequested = _requestedPreviews.Count > 0;
                    WinForms.MessageBox.Show(
                        this,
                        "Não foi possível iniciar o preview para este monitor.",
                        "Preview",
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Warning);
                    return;
                }
            }

            await ShowMonitorTestWindowAsync(context).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _requestedPreviews.Remove(monitorId);
            _previewsRequested = _requestedPreviews.Count > 0;
            WinForms.MessageBox.Show(
                this,
                $"Não foi possível abrir o preview: {ex.Message}",
                "Preview",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
        }
    }

    private async Task ShowMonitorTestWindowAsync(MonitorCardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var displayName = LayoutHelpers.GetMonitorDisplayName(context.Monitor);
        var bounds = ResolveMonitorTestArea(context);

        if (context.TestWindow is not MonitorTestForm window || window.IsDisposed)
        {
            window = new MonitorTestForm(displayName);
            await context.SetTestWindowAsync(window).ConfigureAwait(true);
            window.Bounds = bounds;
            window.Show(this);
            return;
        }

        window.UpdateMonitorName(displayName);
        window.Bounds = bounds;

        if (window.WindowState == WinForms.FormWindowState.Minimized)
        {
            window.WindowState = WinForms.FormWindowState.Normal;
        }

        window.Activate();
    }

    private Drawing.Rectangle ResolveMonitorTestArea(MonitorCardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var candidates = new[]
        {
            NormalizeRectangle(context.Host.MonitorWorkArea),
            NormalizeRectangle(context.Monitor.WorkArea),
            NormalizeRectangle(context.Host.MonitorBounds),
            NormalizeRectangle(context.Monitor.Bounds),
        };

        foreach (var candidate in candidates)
        {
            if (!candidate.IsEmpty)
            {
                return candidate;
            }
        }

        var screen = FindScreenForMonitor(context);
        if (screen is not null)
        {
            var working = NormalizeRectangle(screen.WorkingArea);
            if (!working.IsEmpty)
            {
                return working;
            }

            var bounds = NormalizeRectangle(screen.Bounds);
            if (!bounds.IsEmpty)
            {
                return bounds;
            }
        }

        var fallbackLocation = WinForms.Screen.PrimaryScreen?.WorkingArea.Location ?? new Drawing.Point(100, 100);
        const int fallbackWidth = 800;
        const int fallbackHeight = 600;
        return new Drawing.Rectangle(fallbackLocation, new Drawing.Size(fallbackWidth, fallbackHeight));
    }

    private static Drawing.Rectangle NormalizeRectangle(Drawing.Rectangle rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return Drawing.Rectangle.Empty;
        }

        return rectangle;
    }

    private static WinForms.Screen? FindScreenForMonitor(MonitorCardContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Monitor.DeviceName))
        {
            var byDevice = WinForms.Screen.AllScreens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, context.Monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (byDevice is not null)
            {
                return byDevice;
            }
        }

        foreach (var screen in WinForms.Screen.AllScreens)
        {
            if (screen.Bounds == context.Monitor.Bounds || screen.WorkingArea == context.Monitor.WorkArea)
            {
                return screen;
            }
        }

        return WinForms.Screen.AllScreens.FirstOrDefault();
    }

    private void UpdateSelectedMonitor(string? monitorId, bool notify = true)
    {
        if (string.Equals(SelectedMonitorId, monitorId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateMonitorSelectionVisuals();
            return;
        }

        SelectedMonitorId = monitorId;
        if (_currentProfile is not null)
        {
            _currentProfile = _currentProfile with { DefaultMonitorId = monitorId };
        }
        UpdateMonitorSelectionVisuals();

        if (notify && monitorId is not null)
        {
            MonitorSelected?.Invoke(this, monitorId);
        }
    }

    private void UpdateMonitorSelectionVisuals()
    {
        foreach (var context in _monitorCardOrder)
        {
            var isSelected = SelectedMonitorId is not null &&
                string.Equals(context.MonitorId, SelectedMonitorId, StringComparison.OrdinalIgnoreCase);

            if (context.Card is Controls.MonitorCardPanel monitorCard)
            {
                monitorCard.Selected = isSelected;
            }
            else
            {
                context.Card.BackColor = isSelected
                    ? System.Drawing.SystemColors.GradientInactiveCaption
                    : System.Drawing.SystemColors.Control;
            }
        }
    }

    private async Task StartAutomaticPreviewsAsync()
    {
        _previewsRequested = _requestedPreviews.Count > 0;

        if (!_previewsRequested || WindowState == WinForms.FormWindowState.Minimized)
        {
            return;
        }

        var safeMode = _graphicsOptions.Mode == PreviewGraphicsMode.Gdi;
        var preferGpu = ShouldPreferGpu();

        foreach (var context in _monitorCardOrder)
        {
            if (_manuallyStoppedMonitors.Contains(context.MonitorId)
                || !_requestedPreviews.Contains(context.MonitorId))
            {
                continue;
            }

            context.Host.PreviewSafeModeEnabled = safeMode;

            var started = await context.Host.StartSafeAsync(preferGpu).ConfigureAwait(true);
            if (!started)
            {
                _telemetry.Warn($"Pré-visualização do monitor '{context.MonitorId}' não pôde ser iniciada.");
            }
        }
    }

    private async Task PausePreviewsAsync()
    {
        foreach (var context in _monitorCardOrder)
        {
            await context.Host.StopSafeAsync().ConfigureAwait(true);
        }
    }

    private void StopAutomaticPreviews(bool clearManualState)
    {
        _previewsRequested = false;

        // Fire-and-forget pause instead of blocking the UI thread.
        // StopSafeAsync inside PausePreviewsAsync already handles cancellation gracefully.
        _ = PausePreviewsAsync().ContinueWith(
            t => _telemetry.Warn("Falha ao pausar previews.", t.Exception?.InnerException),
            TaskContinuationOptions.OnlyOnFaulted);

        if (clearManualState)
        {
            _manuallyStoppedMonitors.Clear();
            _requestedPreviews.Clear();
        }
    }

    private void DisposeMonitorCards()
    {
        foreach (var context in _monitorCardOrder)
        {
            _ = context.CloseTestWindowAsync().ContinueWith(
                t => _telemetry.Warn("Falha ao fechar janela de teste durante limpeza dos cards.", t.Exception?.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        foreach (var host in _monitorHosts)
        {
            host.Dispose();
        }

        _monitorCardOrder.Clear();
        _monitorCards.Clear();
        _monitorHosts.Clear();

        if (tlpMonitores is not null)
        {
            tlpMonitores.SuspendLayout();
            tlpMonitores.Controls.Clear();
            tlpMonitores.RowStyles.Clear();
            tlpMonitores.RowCount = 1;
            tlpMonitores.ResumeLayout(false);
        }
    }

    private void dgvProgramas_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateButtonStates();
    }

    private void dgvProgramas_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0)
        {
            btnEditar_Click(sender, EventArgs.Empty);
        }
    }

    private void dgvProgramas_KeyDown(object? sender, WinForms.KeyEventArgs e)
    {
        if (e.KeyCode == WinForms.Keys.Delete)
        {
            btnExcluir_Click(sender, EventArgs.Empty);
            e.Handled = true;
        }
    }



    private void btnAdicionar_Click(object? sender, EventArgs e)
    {
        var novoPrograma = new ProgramaConfig
        {
            Id = GerarIdentificadorUnico("novo_programa"),
            Name = "Novo aplicativo",
            AutoStart = true,
            Window = new WindowConfig
            {
                FullScreen = true,
                Monitor = ResolveDefaultMonitorKey(),
            },
            TargetMonitorStableId = SelectedMonitorId ?? string.Empty,
        };

        AbrirEditorAsync(null, novoPrograma);
    }

    private void btnEditar_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        AbrirEditorAsync(selecionado);
    }

    private void btnDuplicar_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        var novoId = GerarIdentificadorUnico(selecionado.Id + "_copia");
        var copia = selecionado with { Id = novoId };
        _programas.Add(copia);
        SelecionarPrograma(copia);
    }

    private void btnExcluir_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        var confirmacao = WinForms.MessageBox.Show(
            this,
            $"Deseja remover o programa '{selecionado.Id}'?",
            "Excluir Programa",
            WinForms.MessageBoxButtons.YesNo,
            WinForms.MessageBoxIcon.Question);

        if (confirmacao != WinForms.DialogResult.Yes)
        {
            return;
        }

        _programas.Remove(selecionado);
    }

    private async void btnExecutar_Click(object? sender, EventArgs e)
    {
        try
        {
            await ExecutarOrchestratorAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha não tratada ao executar orchestrator.", ex);
        }
    }

    private async void btnParar_Click(object? sender, EventArgs e)
    {
        try
        {
            await PararOrchestratorAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha não tratada ao parar orchestrator.", ex);
        }
    }

    private async Task ExecutarOrchestratorAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtonStates();

        try
        {
            await _orchestrator.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao iniciar o orchestrator.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao iniciar: {ex.Message}", "Orchestrator", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateButtonStates();
        }
    }

    private async Task PararOrchestratorAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtonStates();

        try
        {
            await _orchestrator.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao parar o orchestrator.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao parar: {ex.Message}", "Orchestrator", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateButtonStates();
        }
    }

    private void btnSalvarPerfil_Click(object? sender, EventArgs e)
    {
        SalvarPerfil();
    }

    private async void btnExecutarPerfil_Click(object? sender, EventArgs e)
    {
        try
        {
            await RunProfileAsync(BuildProfileFromUI()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao executar perfil.", ex);
        }
    }

    private async void btnPararPerfil_Click(object? sender, EventArgs e)
    {
        try
        {
            await StopProfileExecutionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao parar perfil.", ex);
        }
    }

    private async void btnTestarItem_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um item para testar.", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        try
        {
            await _appTestRunner.RunTestAsync(selecionado, this).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao testar item.", ex);
        }
    }

    private void menuPerfisSalvar_Click(object? sender, EventArgs e)
    {
        SalvarPerfil();
    }

    private async void menuPerfisExecutar_Click(object? sender, EventArgs e)
    {
        try
        {
            await RunProfileAsync(BuildProfileFromUI()).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao executar perfil via menu.", ex);
        }
    }

    private async void menuPerfisParar_Click(object? sender, EventArgs e)
    {
        try
        {
            await StopProfileExecutionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao parar perfil via menu.", ex);
        }
    }

    private void menuPerfisTestar_Click(object? sender, EventArgs e)
    {
        btnTestarItem_Click(sender, EventArgs.Empty);
    }

    private void menuPerfisCarregar_Click(object? sender, EventArgs e)
    {
        try
        {
            var profiles = _profileStore.ListAll();
            if (profiles.Count == 0)
            {
                WinForms.MessageBox.Show(this, "Nenhum perfil salvo foi encontrado.", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                return;
            }

            if (profiles.Count == 1)
            {
                ApplyProfile(profiles[0]);
                UpdateStatusText($"Perfil '{profiles[0].Name}' carregado.");
                return;
            }

            var options = string.Join(Environment.NewLine, profiles.Select(p => $"{p.Id} - {p.Name}"));
            var selectedId = PromptText(this, "Carregar Perfil", $"Perfis disponíveis:{Environment.NewLine}{options}{Environment.NewLine}{Environment.NewLine}Informe o identificador do perfil:", _currentProfile?.Id ?? string.Empty);
            if (string.IsNullOrWhiteSpace(selectedId))
            {
                return;
            }

            var profile = profiles.FirstOrDefault(p => string.Equals(p.Id, selectedId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                WinForms.MessageBox.Show(this, $"O perfil '{selectedId}' não foi encontrado.", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            ApplyProfile(profile);
            UpdateStatusText($"Perfil '{profile.Name}' carregado.");
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao carregar perfis.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao carregar perfis: {ex.Message}", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuSegurancaUsuarios_Click(object? sender, EventArgs e)
    {
        try
        {
            using var securityDb = new Mieruka.Core.Security.Data.SecurityDbContext();
            var auditLog = new Mieruka.Core.Security.Services.AuditLogService(securityDb);
            var userService = new Mieruka.Core.Security.Services.UserManagementService(securityDb, auditLog);
            var systemUser = new Mieruka.Core.Security.Models.User
            {
                Id = 0,
                Username = "system",
                Role = Mieruka.Core.Security.Models.UserRole.Admin,
            };

            using var form = new UserManagementForm(userService, systemUser);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao abrir gerenciamento de usuários.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao abrir gerenciamento de usuários: {ex.Message}", "Segurança", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuSegurancaCredenciais_Click(object? sender, EventArgs e)
    {
        try
        {
            using var db = new Mieruka.Core.Data.MierukaDbContext();
            var crudService = new Mieruka.Core.Data.Services.SecurityCrudService(db);
            var vault = new Mieruka.Core.Security.CredentialVault();

            using var form = new CredentialsManagementForm(crudService, vault, currentUserId: 0);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao abrir gerenciamento de credenciais.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao abrir gerenciamento de credenciais: {ex.Message}", "Segurança", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuSegurancaAuditoria_Click(object? sender, EventArgs e)
    {
        try
        {
            using var securityDb = new Mieruka.Core.Security.Data.SecurityDbContext();
            var auditLog = new Mieruka.Core.Security.Services.AuditLogService(securityDb);

            using var form = new AuditLogViewerForm(auditLog);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao abrir log de auditoria.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao abrir log de auditoria: {ex.Message}", "Segurança", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuConfiguracaoExportar_Click(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new WinForms.SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                DefaultExt = "json",
                FileName = "mieruka-config.json",
                Title = "Exportar Configuração",
            };

            if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
            {
                return;
            }

            var config = new GeneralConfig
            {
                Applications = _programas.Select(CloneAppConfig).ToList(),
                Monitors = _monitorSnapshot.ToList(),
            };

            var migrator = new Config.ConfigMigrator();
            migrator.ExportToFile(dialog.FileName, config);
            UpdateStatusText("Configuração exportada com sucesso.");
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao exportar configuração.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao exportar: {ex.Message}", "Configuração", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuConfiguracaoImportar_Click(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new WinForms.OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json|Todos (*.*)|*.*",
                Title = "Importar Configuração",
            };

            if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
            {
                return;
            }

            var migrator = new Config.ConfigMigrator();
            var config = migrator.ImportFromFile(dialog.FileName);

            _programas.Clear();
            foreach (var app in config.Applications)
            {
                _programas.Add(CloneAppConfig(app));
            }

            bsProgramas?.ResetBindings(false);
            UpdateStatusText("Configuração importada com sucesso.");
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao importar configuração.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao importar: {ex.Message}", "Configuração", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private async void menuConfiguracaoBackup_Click(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new WinForms.SaveFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db",
                DefaultExt = "db",
                FileName = $"mieruka-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db",
                Title = "Backup do Banco",
            };

            if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
            {
                return;
            }

            var service = new Mieruka.Core.Data.Services.DatabaseBackupService();
            await service.BackupAsync(dialog.FileName).ConfigureAwait(true);
            UpdateStatusText("Backup realizado com sucesso.");
            WinForms.MessageBox.Show(this, "Backup realizado com sucesso.", "Backup", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao realizar backup.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao fazer backup: {ex.Message}", "Backup", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private async void menuConfiguracaoRestaurar_Click(object? sender, EventArgs e)
    {
        try
        {
            using var dialog = new WinForms.OpenFileDialog
            {
                Filter = "SQLite Database (*.db)|*.db|Todos (*.*)|*.*",
                Title = "Restaurar Banco",
            };

            if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
            {
                return;
            }

            var confirm = WinForms.MessageBox.Show(
                this,
                "A restauração substituirá o banco de dados atual. Deseja continuar?",
                "Restaurar Banco",
                WinForms.MessageBoxButtons.YesNo,
                WinForms.MessageBoxIcon.Warning);

            if (confirm != WinForms.DialogResult.Yes)
            {
                return;
            }

            var service = new Mieruka.Core.Data.Services.DatabaseBackupService();
            await service.RestoreAsync(dialog.FileName).ConfigureAwait(true);
            UpdateStatusText("Banco restaurado. Reinicie a aplicação.");
            WinForms.MessageBox.Show(this, "Banco restaurado com sucesso. Reinicie a aplicação para aplicar as alterações.", "Restauração", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao restaurar banco.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao restaurar: {ex.Message}", "Restauração", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuConfiguracaoRetencao_Click(object? sender, EventArgs e)
    {
        try
        {
            using var form = new DataRetentionForm();
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao abrir retenção de dados.", ex);
            WinForms.MessageBox.Show(this, $"Erro: {ex.Message}", "Retenção de Dados", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuConfiguracaoHistorico_Click(object? sender, EventArgs e)
    {
        try
        {
            using var form = new ConfigHistoryForm();
            form.ConfigRestored += (_, config) =>
            {
                _programas.Clear();
                foreach (var app in config.Applications)
                {
                    _programas.Add(CloneAppConfig(app));
                }

                bsProgramas?.ResetBindings(false);
                UpdateStatusText("Configuração restaurada do histórico.");
            };
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao abrir histórico de configuração.", ex);
            WinForms.MessageBox.Show(this, $"Erro: {ex.Message}", "Histórico", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuConfiguracaoDashboard_Click(object? sender, EventArgs e)
    {
        try
        {
            using var form = new StatusDashboardForm(() => Array.Empty<WatchdogStatusEntry>());
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao abrir dashboard.", ex);
            WinForms.MessageBox.Show(this, $"Erro: {ex.Message}", "Dashboard", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private async void menuConfiguracaoAgendamento_Click(object? sender, EventArgs e)
    {
        try
        {
            SchedulerService? scheduler = null;

            try
            {
                scheduler = new SchedulerService(_orchestrator);
            }
            catch (Exception ex)
            {
                _telemetry.Warn("Falha ao criar SchedulerService.", ex);
            }

            var currentConfig = scheduler?.GetCurrentConfig();

            using var form = new ScheduleEditorForm(currentConfig);
            if (form.ShowDialog(this) == WinForms.DialogResult.OK && scheduler is not null)
            {
                await scheduler.ApplyConfigAsync(form.Result).ConfigureAwait(true);
                UpdateStatusText("Agendamento salvo.");
            }

            scheduler?.Dispose();
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao configurar agendamento.", ex);
            WinForms.MessageBox.Show(this, $"Erro: {ex.Message}", "Agendamento", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void menuConfiguracaoModoEscuro_Click(object? sender, EventArgs e)
    {
        try
        {
            var theme = menuConfiguracaoModoEscuro.Checked
                ? Services.ThemeManager.AppTheme.Dark
                : Services.ThemeManager.AppTheme.Light;
            Services.ThemeManager.ApplyTheme(this, theme);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao alternar tema.", ex);
        }
    }

    private void menuConfiguracaoIdiomaPtBr_Click(object? sender, EventArgs e)
    {
        menuConfiguracaoIdiomaPtBr.Checked = true;
        menuConfiguracaoIdiomaEnUs.Checked = false;
        Core.Localization.L.SetCulture("pt-BR");
        UpdateStatusText("Idioma alterado para Português.");
    }

    private void menuConfiguracaoIdiomaEnUs_Click(object? sender, EventArgs e)
    {
        menuConfiguracaoIdiomaPtBr.Checked = false;
        menuConfiguracaoIdiomaEnUs.Checked = true;
        Core.Localization.L.SetCulture("en-US");
        UpdateStatusText("Language changed to English.");
    }

    private void Orchestrator_StateChanged(object? sender, OrchestratorStateChangedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(new WinForms.MethodInvoker(UpdateButtonStates));
        }
        catch (ObjectDisposedException)
        {
            // Ignore because the form is closing.
        }
    }

    private void UpdateButtonStates()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new WinForms.MethodInvoker(UpdateButtonStates));
            return;
        }

        var selecionado = ObterProgramaSelecionado();
        var temSelecao = selecionado is not null;
        var estado = _orchestrator.State;
        var podeExecutar = !_busy && estado is not OrchestratorState.Running and not OrchestratorState.Recovering;
        var podeParar = !_busy && estado is OrchestratorState.Running or OrchestratorState.Recovering;

        btnEditar.Enabled = temSelecao;
        btnDuplicar.Enabled = temSelecao;
        btnExcluir.Enabled = temSelecao;
        btnExecutar.Enabled = podeExecutar;
        btnParar.Enabled = podeParar;

        UpdateProfileUiState();
    }

    private ProgramaConfig? ObterProgramaSelecionado()
    {
        return bsProgramas?.Current as ProgramaConfig;
    }

    private void SelecionarPrograma(ProgramaConfig programa)
    {
        var grid = dgvProgramas;
        if (grid is null)
        {
            return;
        }

        for (var i = 0; i < grid.Rows.Count; i++)
        {
            var row = grid.Rows[i];
            if (row.DataBoundItem is ProgramaConfig atual && ReferenceEquals(atual, programa))
            {
                grid.ClearSelection();
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private void UpdateProfileUiState()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new WinForms.MethodInvoker(UpdateProfileUiState));
            return;
        }

        var hasPrograms = _programas.Count > 0;
        var selected = ObterProgramaSelecionado() is not null;
        var canRun = hasPrograms && !_profileRunning;

        if (btnSalvarPerfil is not null)
        {
            btnSalvarPerfil.Enabled = !_profileRunning;
        }

        if (btnExecutarPerfil is not null)
        {
            btnExecutarPerfil.Enabled = canRun;
        }

        if (btnPararPerfil is not null)
        {
            btnPararPerfil.Enabled = _profileRunning;
        }

        if (btnTestarItem is not null)
        {
            btnTestarItem.Enabled = !_profileRunning && selected;
        }

        if (menuPerfisSalvar is not null)
        {
            menuPerfisSalvar.Enabled = !_profileRunning;
        }

        if (menuPerfisExecutar is not null)
        {
            menuPerfisExecutar.Enabled = canRun;
        }

        if (menuPerfisParar is not null)
        {
            menuPerfisParar.Enabled = _profileRunning;
        }

        if (menuPerfisTestar is not null)
        {
            menuPerfisTestar.Enabled = !_profileRunning && selected;
        }
    }

    private void UpdateStatusText(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(UpdateStatusText), message);
            return;
        }

        if (lblStatus is not null)
        {
            lblStatus.Text = message;
        }
    }

    private ProfileConfig BuildProfileFromUI()
    {
        var id = !string.IsNullOrWhiteSpace(_currentProfile?.Id)
            ? _currentProfile!.Id
            : GenerateDefaultProfileId();
        var name = !string.IsNullOrWhiteSpace(_currentProfile?.Name)
            ? _currentProfile!.Name
            : "Perfil atual";
        var defaultMonitor = SelectedMonitorId ?? _currentProfile?.DefaultMonitorId;

        var apps = new List<AppConfig>(_programas.Count);
        foreach (var programa in _programas)
        {
            apps.Add(CloneAppConfig(programa));
        }

        var windows = _currentProfile?.Windows?.Select(CloneWindowConfig).ToList() ?? new List<WindowConfig>();

        return new ProfileConfig
        {
            Id = id,
            Name = name,
            SchemaVersion = 1,
            DefaultMonitorId = defaultMonitor,
            Applications = apps,
            Windows = windows,
        };
    }

    private ProfileConfig ProfileFromSelection(ProgramaConfig selected)
    {
        var profile = BuildProfileFromUI();
        var apps = new List<AppConfig> { CloneAppConfig(selected) };
        return profile with { Applications = apps };
    }

    private static AppConfig CloneAppConfig(AppConfig app)
    {
        var environment = new Dictionary<string, string>(app.EnvironmentVariables, StringComparer.OrdinalIgnoreCase);
        var window = CloneWindowConfig(app.Window);
        var watchdog = app.Watchdog with
        {
            HealthCheck = app.Watchdog.HealthCheck is null ? null : app.Watchdog.HealthCheck with { },
        };

        return app with
        {
            EnvironmentVariables = environment,
            Watchdog = watchdog,
            Window = window,
        };
    }

    private static WindowConfig CloneWindowConfig(WindowConfig window)
        => window with { Monitor = window.Monitor with { } };

    private bool EnsureProfileIdentity()
    {
        if (_currentProfile is not null &&
            !string.IsNullOrWhiteSpace(_currentProfile.Id) &&
            !string.IsNullOrWhiteSpace(_currentProfile.Name))
        {
            return true;
        }

        var defaultName = _currentProfile?.Name;
        var name = PromptText(this, "Salvar Perfil", "Informe um nome para o perfil:", string.IsNullOrWhiteSpace(defaultName) ? "Perfil" : defaultName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var defaultId = NormalizeProfileId(name);
        var id = PromptText(this, "Salvar Perfil", "Informe um identificador para o perfil:", defaultId);
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        id = NormalizeProfileId(id);
        if (string.IsNullOrWhiteSpace(id))
        {
            WinForms.MessageBox.Show(this, "Informe um identificador válido.", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return false;
        }

        var windows = _currentProfile?.Windows?.Select(CloneWindowConfig).ToList() ?? new List<WindowConfig>();
        var defaultMonitor = _currentProfile?.DefaultMonitorId;

        _currentProfile = new ProfileConfig
        {
            Id = id,
            Name = name.Trim(),
            DefaultMonitorId = defaultMonitor,
            Windows = windows,
            Applications = new List<AppConfig>(),
        };

        return true;
    }

    private static string NormalizeProfileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = ProfileIdSanitizer.Replace(value.Trim(), "_");
        sanitized = sanitized.Trim('_');
        if (sanitized.Length == 0)
        {
            sanitized = "perfil";
        }

        return sanitized.ToLowerInvariant();
    }

    private static string GenerateDefaultProfileId() => DefaultProfileId;

    private static string? PromptText(IWin32Window owner, string title, string message, string? defaultValue)
    {
        using var prompt = new WinForms.Form
        {
            Text = title,
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog,
            StartPosition = WinForms.FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Drawing.Size(360, 140),
        };

        var label = new WinForms.Label
        {
            AutoSize = true,
            Text = message,
            Location = new Drawing.Point(12, 12),
        };

        var textBox = new WinForms.TextBox
        {
            Location = new Drawing.Point(12, label.Bottom + 8),
            Width = prompt.ClientSize.Width - 24,
            Text = defaultValue ?? string.Empty,
            Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right,
        };

        var okButton = new WinForms.Button
        {
            Text = "OK",
            DialogResult = WinForms.DialogResult.OK,
            Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right,
            Location = new Drawing.Point(prompt.ClientSize.Width - 180, prompt.ClientSize.Height - 40),
            Size = new Drawing.Size(80, 27),
        };

        var cancelButton = new WinForms.Button
        {
            Text = "Cancelar",
            DialogResult = WinForms.DialogResult.Cancel,
            Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right,
            Location = new Drawing.Point(prompt.ClientSize.Width - 92, prompt.ClientSize.Height - 40),
            Size = new Drawing.Size(80, 27),
        };

        prompt.Controls.Add(label);
        prompt.Controls.Add(textBox);
        prompt.Controls.Add(okButton);
        prompt.Controls.Add(cancelButton);
        prompt.AcceptButton = okButton;
        prompt.CancelButton = cancelButton;

        return prompt.ShowDialog(owner) == WinForms.DialogResult.OK ? textBox.Text : null;
    }

    private void LoadProfileFromStore()
    {
        try
        {
            var profiles = _profileStore.ListAll();
            if (profiles.Count == 0)
            {
                UpdateProfileUiState();
                return;
            }

            ApplyProfile(profiles[0]);
            UpdateStatusText($"Perfil '{profiles[0].Name}' carregado.");
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao carregar perfis.", ex);
        }
    }

    private void ApplyProfile(ProfileConfig profile)
    {
        var applications = profile.Applications.Select(CloneAppConfig).ToList();
        var windows = profile.Windows.Select(CloneWindowConfig).ToList();

        _currentProfile = profile with
        {
            Applications = applications,
            Windows = windows,
        };

        _programas.Clear();
        foreach (var app in applications)
        {
            _programas.Add(CloneAppConfig(app));
        }

        bsProgramas?.ResetBindings(false);

        if (!string.IsNullOrWhiteSpace(profile.DefaultMonitorId))
        {
            UpdateSelectedMonitor(profile.DefaultMonitorId, notify: false);
        }

        UpdateProfileUiState();
    }

    private ProfileExecutor CreateProfileExecutor()
    {
        var networkService = new NetworkAvailabilityService();
        var dialogHost = new WinFormsDialogHost(this);
        var executor = new ProfileExecutor(
            displayService: _displayService,
            networkAvailabilityService: networkService,
            dialogHost: dialogHost);
        executor.AppStarted += ProfileExecutor_AppStarted;
        executor.AppPositioned += ProfileExecutor_AppPositioned;
        executor.Completed += ProfileExecutor_Completed;
        executor.Failed += ProfileExecutor_Failed;
        return executor;
    }

    private async Task RunProfileAsync(ProfileConfig profile)
    {
        if (_profileRunning)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            WinForms.MessageBox.Show(this, "A execução de perfis está disponível apenas no Windows.", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        if (profile.Applications.Count == 0)
        {
            WinForms.MessageBox.Show(this, "Adicione ao menos um aplicativo ao perfil antes de executar.", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        _profileExecutor ??= CreateProfileExecutor();

        _profileExecutionCts?.Dispose();
        _profileExecutionCts = new CancellationTokenSource();

        _profileRunning = true;
        UpdateProfileUiState();
        UpdateStatusText($"Executando perfil '{profile.Name}'...");

        try
        {
            _profileExecutionTask = _profileExecutor.Start(profile, _profileExecutionCts.Token);
            await _profileExecutionTask.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha durante a execução do perfil.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao executar o perfil: {ex.Message}", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            _profileExecutionTask = null;
            _profileExecutionCts?.Dispose();
            _profileExecutionCts = null;
            _profileRunning = false;
            UpdateProfileUiState();
        }
    }

    private async Task StopProfileExecutionAsync()
    {
        if (!_profileRunning)
        {
            return;
        }

        _profileExecutor?.Stop();
        _profileExecutionCts?.Cancel();
        UpdateStatusText("Cancelando execução do perfil...");

        if (_profileExecutionTask is Task task)
        {
            try
            {
                await task.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _telemetry.Warn("Falha ao aguardar o cancelamento do perfil.", ex);
            }
        }
    }

    private void SalvarPerfil()
    {
        if (!EnsureProfileIdentity())
        {
            return;
        }

        var profile = BuildProfileFromUI();

        try
        {
            _profileStore.Save(profile);
            _currentProfile = profile;
            UpdateStatusText($"Perfil '{profile.Name}' salvo.");
        }
        catch (Exception ex)
        {
            _telemetry.Error("Falha ao salvar perfil.", ex);
            WinForms.MessageBox.Show(this, $"Erro ao salvar perfil: {ex.Message}", "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            UpdateProfileUiState();
        }
    }

    private void ProfileExecutor_AppStarted(object? sender, AppExecutionEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ProfileExecutor_AppStarted(sender, e)));
            return;
        }

        var name = string.IsNullOrWhiteSpace(e.DisplayName) ? "aplicativo" : e.DisplayName;
        UpdateStatusText($"Iniciando {name}...");
    }

    private void ProfileExecutor_AppPositioned(object? sender, AppExecutionEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ProfileExecutor_AppPositioned(sender, e)));
            return;
        }

        var name = string.IsNullOrWhiteSpace(e.DisplayName) ? "janela" : e.DisplayName;
        if (e.Monitor is not null && !string.IsNullOrWhiteSpace(e.Monitor.Name))
        {
            UpdateStatusText($"'{name}' posicionado em {e.Monitor.Name}.");
        }
        else
        {
            UpdateStatusText($"'{name}' posicionado.");
        }
    }

    private void ProfileExecutor_Completed(object? sender, ProfileExecutionCompletedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ProfileExecutor_Completed(sender, e)));
            return;
        }

        UpdateStatusText(e.Cancelled ? "Execução cancelada." : "Execução concluída.");
    }

    private void ProfileExecutor_Failed(object? sender, ProfileExecutionFailedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ProfileExecutor_Failed(sender, e)));
            return;
        }

        var target = e.Application?.Id ?? e.Window?.Title ?? e.Profile.Name;
        var message = $"Falha ao posicionar '{target}': {e.Exception.Message}";
        UpdateStatusText(message);
        WinForms.MessageBox.Show(this, message, "Perfis", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
    }

    private string GerarIdentificadorUnico(string baseId)
    {
        var existingIds = new HashSet<string>(
            _programas.Select(p => p.Id),
            StringComparer.OrdinalIgnoreCase);
        var id = baseId;
        var contador = 1;
        while (existingIds.Contains(id))
        {
            id = $"{baseId}_{contador++}";
        }

        return id;
    }

    private MonitorKey ResolveDefaultMonitorKey()
    {
        if (!string.IsNullOrWhiteSpace(SelectedMonitorId))
        {
            var normalized = MonitorIdentifier.Normalize(SelectedMonitorId);
            var selectedMonitor = _monitorSnapshot.FirstOrDefault(m =>
                string.Equals(MonitorIdentifier.Normalize(MonitorIdentifier.Create(m)), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(MonitorIdentifier.Normalize(m.StableId), normalized, StringComparison.OrdinalIgnoreCase));

            if (selectedMonitor is not null)
            {
                return selectedMonitor.Key;
            }
        }

        return _monitorSnapshot.FirstOrDefault(m => m.IsPrimary)?.Key
            ?? _monitorSnapshot.FirstOrDefault()?.Key
            ?? new MonitorKey();
    }

    private async void AbrirEditorAsync(ProgramaConfig? selected, ProgramaConfig? template = null)
    {
        try
        {
            var monitors = _monitorSnapshot.Count > 0
                ? _monitorSnapshot.ToList()
                : CaptureMonitorSnapshot().ToList();

            var programaParaEdicao = template ?? selected;

            // Stop main form previews before opening the editor to avoid
            // GPU capture contention (WGC only allows one session per monitor).
            // Without this, two capture clients fight for the same device causing
            // heavy flickering and forced closure of GPU-backed applications.
            // PausePreviewsAsync calls StopSafeAsync which fully releases the
            // WGC session — SuspendCaptureAsync is not enough as it keeps the
            // session handle open.
            var hadPreviews = _previewsRequested;

            try
            {
                await PausePreviewsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _telemetry.Warn("Falha ao pausar previews antes de abrir editor.", ex);
            }

            WinForms.DialogResult resultado;
            string? editorSelectedMonitor;
            ProgramaConfig? programa;

            try
            {
                using var editor = new AppEditorForm(programaParaEdicao, monitors, SelectedMonitorId, _appRunner, _programas);
                resultado = editor.ShowDialog(this);
                editorSelectedMonitor = editor.SelectedMonitorId;
                programa = editor.Resultado;
            }
            finally
            {
                // Restart main form previews after the editor closes.
                if (hadPreviews && !IsDisposed)
                {
                    _previewsRequested = true;
                    await StartAutomaticPreviewsAsync().ConfigureAwait(true);
                }
            }

            if (resultado != WinForms.DialogResult.OK)
            {
                return;
            }

            if (programa is null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(editorSelectedMonitor))
            {
                UpdateSelectedMonitor(editorSelectedMonitor, notify: false);
            }

            if (selected is null)
            {
                _programas.Add(programa);
                SelecionarPrograma(programa);
                return;
            }

            var indice = _programas.IndexOf(selected);
            if (indice >= 0)
            {
                _programas[indice] = programa;
                bsProgramas.ResetBindings(false);
                SelecionarPrograma(programa);
            }
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha não tratada ao abrir editor de aplicativo.", ex);
        }
    }

    private async void AppRunnerOnBeforeMoveWindow(object? sender, EventArgs e)
    {
        try
        {
            await SuspendMonitorPreviewsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao suspender previews antes de mover janela.", ex);
        }
    }

    private void AppRunnerOnAfterMoveWindow(object? sender, EventArgs e)
    {
        ScheduleMonitorPreviewResume();
    }

    private async Task SuspendMonitorPreviewsAsync()
    {
        foreach (var host in _monitorHosts.ToArray())
        {
            try
            {
                await host.SuspendCaptureAsync().ConfigureAwait(true);
            }
            catch
            {
                // Ignorar falhas ao suspender a captura durante movimentações de janela.
            }
        }
    }

    private void ScheduleMonitorPreviewResume()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(ResumeMonitorPreviewsWithDelay));
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

    private async void ResumeMonitorPreviewsWithDelay()
    {
        try
        {
            try
            {
                await Task.Delay(MonitorPreviewResumeDelay).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (IsDisposed)
            {
                return;
            }

            await ResumeMonitorPreviewsAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao retomar previews de monitor com atraso.", ex);
        }
    }

    private async Task ResumeMonitorPreviewsAsync()
    {
        foreach (var host in _monitorHosts.ToArray())
        {
            try
            {
                await Task.Run(() => host.ResumeCapture()).ConfigureAwait(true);
            }
            catch
            {
                // Ignorar falhas ao retomar a captura.
            }
        }
    }

    private void MainForm_Disposed(object? sender, EventArgs e)
    {
        _appRunner.BeforeMoveWindow -= AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow -= AppRunnerOnAfterMoveWindow;
    }

    private readonly struct MonitorCardSource
    {
        public MonitorCardSource(MonitorInfo monitor, MonitorDescriptor? descriptor)
        {
            Monitor = monitor;
            Descriptor = descriptor;
        }

        public MonitorInfo Monitor { get; }

        public MonitorDescriptor? Descriptor { get; }
    }

    private sealed partial class MonitorCardContext
    {
        public MonitorCardContext(string monitorId, MonitorInfo monitor, WinForms.Panel card, MonitorPreviewHost host)
        {
            MonitorId = monitorId ?? throw new ArgumentNullException(nameof(monitorId));
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public string MonitorId { get; }

        public MonitorInfo Monitor { get; }

        public WinForms.Panel Card { get; }

        public MonitorPreviewHost Host { get; }

        public MonitorTestForm? TestWindow { get; private set; }

        private bool _pausedForTestWindow;

        public async Task SetTestWindowAsync(MonitorTestForm window)
        {
            ArgumentNullException.ThrowIfNull(window);

            if (ReferenceEquals(TestWindow, window))
            {
                return;
            }

            CloseTestWindow();

            TestWindow = window;
            TestWindow.FormClosed += OnTestWindowClosed;
            await PauseHostForTestWindowAsync().ConfigureAwait(true);
        }

        public async Task CloseTestWindowAsync()
        {
            if (TestWindow is not { IsDisposed: false } window)
            {
                TestWindow = null;
                await ResumeHostFromTestWindowAsync().ConfigureAwait(true);
                return;
            }

            TestWindow = null;
            window.FormClosed -= OnTestWindowClosed;

            try
            {
                window.Close();
            }
            catch
            {
                // Ignorar falhas ao fechar a janela de teste.
            }
            finally
            {
                await ResumeHostFromTestWindowAsync().ConfigureAwait(true);
            }
        }

        private async void OnTestWindowClosed(object? sender, WinForms.FormClosedEventArgs e)
        {
            if (sender is not MonitorTestForm window)
            {
                return;
            }

            window.FormClosed -= OnTestWindowClosed;
            if (ReferenceEquals(window, TestWindow))
            {
                TestWindow = null;
            }

            try
            {
                await ResumeHostFromTestWindowAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Falha ao retomar host após fechar janela de teste.");
            }
        }

        private async Task PauseHostForTestWindowAsync()
        {
            if (_pausedForTestWindow)
            {
                return;
            }

            try
            {
                await Host.PauseAsync().ConfigureAwait(true);
                _pausedForTestWindow = true;
            }
            catch
            {
                _pausedForTestWindow = false;
            }
        }

        private async Task ResumeHostFromTestWindowAsync()
        {
            if (!_pausedForTestWindow)
            {
                return;
            }

            _pausedForTestWindow = false;

            try
            {
                await Host.ResumeAsync().ConfigureAwait(true);
            }
            catch
            {
                // Ignorar falhas ao retomar o host após o teste.
            }
        }
    }

    private sealed class NoOpComponent : IOrchestrationComponent
    {
        public Task StartAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RecoverAsync(System.Threading.CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class UiTelemetry : ITelemetry
    {
        private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<MainForm>();

        public void Info(string message, Exception? exception = null)
        {
            Logger.Information(exception, message);
        }

        public void Warn(string message, Exception? exception = null)
        {
            Logger.Warning(exception, message);
        }

        public void Error(string message, Exception? exception = null)
        {
            Logger.Error(exception, message);
        }
    }
}
