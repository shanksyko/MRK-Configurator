#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Services.Ui;
using Mieruka.App.Ui;
using Mieruka.App.Services;
using Mieruka.Automation.Execution;
using Mieruka.Core.Config;
using Mieruka.Core.Models;
using Mieruka.Core.Monitors;
using Mieruka.Core.Services;
using Mieruka.App.Ui.PreviewBindings;
using ProgramaConfig = Mieruka.Core.Models.AppConfig;

namespace Mieruka.App.Forms;

public partial class MainForm : Form
{
    private readonly BindingList<ProgramaConfig> _programas = new();
    private readonly ITelemetry _telemetry = new UiTelemetry();
    private readonly Orchestrator _orchestrator;
    private readonly IAppRunner _appRunner;
    private bool _busy;
    private readonly List<MonitorInfo> _monitorSnapshot = new();
    private readonly List<MonitorCardContext> _monitorCardOrder = new();
    private readonly IMonitorService _monitorService = new MonitorService();
    private readonly List<MonitorPreviewHost> _monitorHosts = new();
    private readonly Dictionary<string, MonitorCardContext> _monitorCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _manuallyStoppedMonitors = new(StringComparer.OrdinalIgnoreCase);
    private IDisplayService? _displayService;
    private bool _previewsRequested;
    private readonly ProfileStore _profileStore = new();
    private readonly JsonStore<PreviewGraphicsOptions> _graphicsOptionsStore;
    private PreviewGraphicsOptions _graphicsOptions;
    private ToolStrip? _toolStrip;
    private ToolStripDropDownButton? _graphicsDropDown;
    private ToolStripMenuItem? _graphicsAutoItem;
    private ToolStripMenuItem? _graphicsGpuItem;
    private ToolStripMenuItem? _graphicsGdiItem;
    private ProfileExecutor? _profileExecutor;
    private CancellationTokenSource? _profileExecutionCts;
    private Task? _profileExecutionTask;
    private bool _profileRunning;
    private ProfileConfig? _currentProfile;
    private static readonly Regex ProfileIdSanitizer = new("[^A-Za-z0-9_-]+", RegexOptions.Compiled);
    private const string DefaultProfileId = "workspace";
    private static readonly TimeSpan MonitorPreviewResumeDelay = TimeSpan.FromMilliseconds(150);

    public string? SelectedMonitorId { get; private set; }

    public event EventHandler<string>? MonitorSelected;

    public MainForm()
    {
        _graphicsOptionsStore = CreateGraphicsOptionsStore();
        _graphicsOptions = LoadGraphicsOptions();

        InitializeComponent();
        EnsureToolbarWithOptionsButton();
        ToolTipTamer.Tame(this, components);

        _appRunner = new AppRunner();
        _appRunner.BeforeMoveWindow += AppRunnerOnBeforeMoveWindow;
        _appRunner.AfterMoveWindow += AppRunnerOnAfterMoveWindow;

        UpdateStatusText("Pronto");

        var grid = dgvProgramas ?? throw new InvalidOperationException("O DataGridView de programas não foi criado pelo designer.");
        var source = bsProgramas ?? throw new InvalidOperationException("A BindingSource de programas não foi inicializada.");
        _ = menuPreview ?? throw new InvalidOperationException("O menu de preview não foi criado.");
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
        _toolStrip = Controls.OfType<ToolStrip>().FirstOrDefault();
        if (_toolStrip == null)
        {
            _toolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                RenderMode = ToolStripRenderMode.System,
                Dock = DockStyle.Top
            };
            Controls.Add(_toolStrip);
            _toolStrip.BringToFront();
        }

        const string dropDownName = "previewGraphicsDropDown";
        _graphicsDropDown = _toolStrip.Items
            .OfType<ToolStripDropDownButton>()
            .FirstOrDefault(b => string.Equals(b.Name, dropDownName, StringComparison.Ordinal));

        if (_graphicsDropDown is null)
        {
            _graphicsDropDown = new ToolStripDropDownButton
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

        _graphicsAutoItem = CreateGraphicsMenuItem("Auto", PreviewGraphicsMode.Auto, "Seleciona automaticamente entre GPU e GDI");
        _graphicsGpuItem = CreateGraphicsMenuItem("Ativada (GPU)", PreviewGraphicsMode.Gpu, "Força o uso de GPU sempre que possível");
        _graphicsGdiItem = CreateGraphicsMenuItem("Desativada (GDI)", PreviewGraphicsMode.Gdi, "Força o modo GDI para o preview");

        _graphicsDropDown.DropDownItems.AddRange(new ToolStripItem[]
        {
            _graphicsAutoItem,
            _graphicsGpuItem,
            _graphicsGdiItem
        });

        UpdateGraphicsMenuSelection(_graphicsOptions.Mode);
    }

    private ToolStripMenuItem CreateGraphicsMenuItem(string text, PreviewGraphicsMode mode, string toolTip)
    {
        var item = new ToolStripMenuItem
        {
            Text = text,
            ToolTipText = toolTip,
            Tag = mode,
            CheckOnClick = false,
        };
        item.Click += OnGraphicsModeMenuItemClick;
        return item;
    }

    private void OnGraphicsModeMenuItemClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not PreviewGraphicsMode mode)
        {
            return;
        }

        SetGraphicsMode(mode);
    }

    private void ApplyGraphicsOptions()
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
                    && !_manuallyStoppedMonitors.Contains(context.MonitorId)
                    && WindowState != FormWindowState.Minimized);

            if (!shouldRestart)
            {
                continue;
            }

            context.Host.Stop();

            if (_manuallyStoppedMonitors.Contains(context.MonitorId)
                || WindowState == FormWindowState.Minimized)
            {
                continue;
            }

            try
            {
                if (!context.Host.Start(preferGpu))
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

    private void SetGraphicsMode(PreviewGraphicsMode mode)
    {
        if (_graphicsOptions.Mode == mode)
        {
            UpdateGraphicsMenuSelection(mode);
            return;
        }

        _graphicsOptions = _graphicsOptions with { Mode = mode };
        ApplyGraphicsOptions();
    }

    private bool ShouldPreferGpu()
        => _graphicsOptions.Mode != PreviewGraphicsMode.Gdi;

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
            var options = _graphicsOptionsStore.LoadAsync().GetAwaiter().GetResult();
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
        try
        {
            _graphicsOptionsStore.SaveAsync(options.Normalize()).GetAwaiter().GetResult();
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
            BeginInvoke(new Action(RefreshMonitorCards));
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

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        RefreshMonitorCards();
        StartAutomaticPreviews();
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            PausePreviews();
            return;
        }

        if (_previewsRequested)
        {
            StartAutomaticPreviews();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        StopAutomaticPreviews(clearManualState: true);
        DisposeMonitorCards();

        if (_profileRunning)
        {
            try
            {
                _profileExecutor?.Stop();
                _profileExecutionCts?.Cancel();
                _profileExecutionTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore failures while stopping background execution during shutdown.
            }
        }

        _profileExecutionCts?.Dispose();
        _profileExecutionCts = null;
        _profileExecutionTask = null;
        _profileExecutor?.Dispose();
        _profileExecutor = null;

        if (_displayService is not null)
        {
            _displayService.TopologyChanged -= DisplayService_TopologyChanged;
            _displayService.Dispose();
            _displayService = null;
        }
    }

    private void tlpMonitores_SizeChanged(object? sender, EventArgs e)
    {
        UpdateMonitorColumns();
    }

    private void RefreshMonitorCards()
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

        var shouldRestart = _previewsRequested && WindowState != FormWindowState.Minimized;

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

            _monitorCardOrder.Add(context);
            _monitorCards[monitorId] = context;
            _monitorHosts.Add(host);

            if (shouldRestart && !_manuallyStoppedMonitors.Contains(monitorId))
            {
                try
                {
                    if (!host.Start(preferGpu))
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

        var deviceId = monitor.Key?.DeviceId;
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
        var screens = Screen.AllScreens;
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
            tlpMonitores.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));
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
                tlpMonitores.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            tlpMonitores.Controls.Add(context.Card, column, row);
            column++;
        }

        tlpMonitores.RowCount = Math.Max(1, row + (column > 0 ? 1 : 0));
        tlpMonitores.ResumeLayout(true);
        UpdateMonitorSelectionVisuals();
    }

    private static void ApplyMonitorId(Control control, string monitorId)
    {
        control.Tag = monitorId;
        foreach (Control child in control.Controls)
        {
            ApplyMonitorId(child, monitorId);
        }
    }

    private void OnMonitorCardSelected(object? sender, EventArgs e)
    {
        if (sender is not Control control || control.Tag is not string monitorId)
        {
            return;
        }

        if (_monitorCards.ContainsKey(monitorId))
        {
            UpdateSelectedMonitor(monitorId);
        }
    }

    private void OnMonitorCardStopRequested(object? sender, EventArgs e)
    {
        if (sender is not Control control || control.Tag is not string monitorId)
        {
            return;
        }

        if (_monitorCards.TryGetValue(monitorId, out var context))
        {
            context.Host.Stop();
            context.CloseTestWindow();
        }

        _manuallyStoppedMonitors.Add(monitorId);
    }

    private void OnMonitorCardTestRequested(object? sender, EventArgs e)
    {
        if (sender is not Control control || control.Tag is not string monitorId)
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

            context.Host.PreviewSafeModeEnabled = _graphicsOptions.Mode == PreviewGraphicsMode.Gdi;
            if (context.Host.Capture is null)
            {
                if (!context.Host.Start(preferGpu: ShouldPreferGpu()))
                {
                    MessageBox.Show(
                        this,
                        "Não foi possível iniciar o preview para este monitor.",
                        "Preview",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            ShowMonitorTestWindow(context);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Não foi possível abrir o preview: {ex.Message}",
                "Preview",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowMonitorTestWindow(MonitorCardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var displayName = LayoutHelpers.GetMonitorDisplayName(context.Monitor);
        var bounds = ResolveMonitorTestArea(context);

        if (context.TestWindow is not MonitorTestForm window || window.IsDisposed)
        {
            window = new MonitorTestForm(displayName);
            context.SetTestWindow(window);
            window.Bounds = bounds;
            window.Show(this);
            return;
        }

        window.UpdateMonitorName(displayName);
        window.Bounds = bounds;

        if (window.WindowState == FormWindowState.Minimized)
        {
            window.WindowState = FormWindowState.Normal;
        }

        window.Activate();
    }

    private Rectangle ResolveMonitorTestArea(MonitorCardContext context)
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

        var fallbackLocation = Screen.PrimaryScreen?.WorkingArea.Location ?? new Point(100, 100);
        const int fallbackWidth = 800;
        const int fallbackHeight = 600;
        return new Rectangle(fallbackLocation, new Size(fallbackWidth, fallbackHeight));
    }

    private static Rectangle NormalizeRectangle(Rectangle rectangle)
    {
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return Rectangle.Empty;
        }

        return rectangle;
    }

    private static Screen? FindScreenForMonitor(MonitorCardContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Monitor.DeviceName))
        {
            var byDevice = Screen.AllScreens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, context.Monitor.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (byDevice is not null)
            {
                return byDevice;
            }
        }

        foreach (var screen in Screen.AllScreens)
        {
            if (screen.Bounds == context.Monitor.Bounds || screen.WorkingArea == context.Monitor.WorkArea)
            {
                return screen;
            }
        }

        return Screen.AllScreens.FirstOrDefault();
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

            if (context.Card is Forms.Controls.MonitorCardPanel monitorCard)
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

    private void StartAutomaticPreviews()
    {
        _previewsRequested = true;

        if (WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var safeMode = _graphicsOptions.Mode == PreviewGraphicsMode.Gdi;
        var preferGpu = ShouldPreferGpu();

        foreach (var context in _monitorCardOrder)
        {
            if (_manuallyStoppedMonitors.Contains(context.MonitorId))
            {
                continue;
            }

            context.Host.PreviewSafeModeEnabled = safeMode;

            if (!context.Host.Start(preferGpu))
            {
                _telemetry.Warn($"Pré-visualização do monitor '{context.MonitorId}' não pôde ser iniciada.");
            }
        }
    }

    private void PausePreviews()
    {
        foreach (var context in _monitorCardOrder)
        {
            context.Host.Stop();
        }
    }

    private void StopAutomaticPreviews(bool clearManualState)
    {
        _previewsRequested = false;
        PausePreviews();

        if (clearManualState)
        {
            _manuallyStoppedMonitors.Clear();
        }
    }

    private void DisposeMonitorCards()
    {
        foreach (var context in _monitorCardOrder)
        {
            context.CloseTestWindow();
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
            tlpMonitores.Controls.Clear();
            tlpMonitores.RowStyles.Clear();
            tlpMonitores.RowCount = 1;
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

    private void dgvProgramas_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            btnExcluir_Click(sender, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void menuPreview_Click(object? sender, EventArgs e)
    {
        try
        {
            var preview = new PreviewForm();
            preview.Show(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o preview: {ex.Message}", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void btnAdicionar_Click(object? sender, EventArgs e)
    {
        AbrirEditor(null);
    }

    private void btnEditar_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            return;
        }

        AbrirEditor(selecionado);
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

        var confirmacao = MessageBox.Show(
            this,
            $"Deseja remover o programa '{selecionado.Id}'?",
            "Excluir Programa",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmacao != DialogResult.Yes)
        {
            return;
        }

        _programas.Remove(selecionado);
    }

    private async void btnExecutar_Click(object? sender, EventArgs e)
    {
        await ExecutarOrchestratorAsync().ConfigureAwait(false);
    }

    private async void btnParar_Click(object? sender, EventArgs e)
    {
        await PararOrchestratorAsync().ConfigureAwait(false);
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
            MessageBox.Show(this, $"Erro ao iniciar: {ex.Message}", "Orchestrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, $"Erro ao parar: {ex.Message}", "Orchestrator", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        await RunProfileAsync(BuildProfileFromUI()).ConfigureAwait(true);
    }

    private async void btnPararPerfil_Click(object? sender, EventArgs e)
    {
        await StopProfileExecutionAsync().ConfigureAwait(true);
    }

    private async void btnTestarItem_Click(object? sender, EventArgs e)
    {
        var selecionado = ObterProgramaSelecionado();
        if (selecionado is null)
        {
            MessageBox.Show(this, "Selecione um item para testar.", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var profile = ProfileFromSelection(selecionado);
        await RunProfileAsync(profile).ConfigureAwait(true);
    }

    private void menuPerfisSalvar_Click(object? sender, EventArgs e)
    {
        SalvarPerfil();
    }

    private async void menuPerfisExecutar_Click(object? sender, EventArgs e)
    {
        await RunProfileAsync(BuildProfileFromUI()).ConfigureAwait(true);
    }

    private async void menuPerfisParar_Click(object? sender, EventArgs e)
    {
        await StopProfileExecutionAsync().ConfigureAwait(true);
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
                MessageBox.Show(this, "Nenhum perfil salvo foi encontrado.", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                MessageBox.Show(this, $"O perfil '{selectedId}' não foi encontrado.", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyProfile(profile);
            UpdateStatusText($"Perfil '{profile.Name}' carregado.");
        }
        catch (Exception ex)
        {
            _telemetry.Warn("Falha ao carregar perfis.", ex);
            MessageBox.Show(this, $"Erro ao carregar perfis: {ex.Message}", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Orchestrator_StateChanged(object? sender, OrchestratorStateChangedEventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(new MethodInvoker(UpdateButtonStates));
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
            BeginInvoke(new MethodInvoker(UpdateButtonStates));
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
            BeginInvoke(new MethodInvoker(UpdateProfileUiState));
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
            MessageBox.Show(this, "Informe um identificador válido.", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        using var prompt = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 140),
        };

        var label = new Label
        {
            AutoSize = true,
            Text = message,
            Location = new Point(12, 12),
        };

        var textBox = new TextBox
        {
            Location = new Point(12, label.Bottom + 8),
            Width = prompt.ClientSize.Width - 24,
            Text = defaultValue ?? string.Empty,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(prompt.ClientSize.Width - 180, prompt.ClientSize.Height - 40),
            Size = new Size(80, 27),
        };

        var cancelButton = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            Location = new Point(prompt.ClientSize.Width - 92, prompt.ClientSize.Height - 40),
            Size = new Size(80, 27),
        };

        prompt.Controls.Add(label);
        prompt.Controls.Add(textBox);
        prompt.Controls.Add(okButton);
        prompt.Controls.Add(cancelButton);
        prompt.AcceptButton = okButton;
        prompt.CancelButton = cancelButton;

        return prompt.ShowDialog(owner) == DialogResult.OK ? textBox.Text : null;
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
            MessageBox.Show(this, "A execução de perfis está disponível apenas no Windows.", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (profile.Applications.Count == 0)
        {
            MessageBox.Show(this, "Adicione ao menos um aplicativo ao perfil antes de executar.", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show(this, $"Erro ao executar o perfil: {ex.Message}", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, $"Erro ao salvar perfil: {ex.Message}", "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        MessageBox.Show(this, message, "Perfis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private string GerarIdentificadorUnico(string baseId)
    {
        var id = baseId;
        var contador = 1;
        while (_programas.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}_{contador++}";
        }

        return id;
    }

    private void AbrirEditor(ProgramaConfig? selected)
    {
        var monitors = _monitorSnapshot.Count > 0
            ? _monitorSnapshot.ToList()
            : CaptureMonitorSnapshot().ToList();

        using var editor = new AppEditorForm(selected, monitors, SelectedMonitorId, _appRunner, _programas);
        var resultado = editor.ShowDialog(this);
        if (resultado != DialogResult.OK)
        {
            return;
        }

        var programa = editor.Resultado;
        if (programa is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(editor.SelectedMonitorId))
        {
            UpdateSelectedMonitor(editor.SelectedMonitorId, notify: false);
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

    private void AppRunnerOnBeforeMoveWindow(object? sender, EventArgs e)
    {
        SuspendMonitorPreviews();
    }

    private void AppRunnerOnAfterMoveWindow(object? sender, EventArgs e)
    {
        ScheduleMonitorPreviewResume();
    }

    private void SuspendMonitorPreviews()
    {
        foreach (var host in _monitorHosts.ToArray())
        {
            try
            {
                host.SuspendCapture();
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
            await Task.Delay(MonitorPreviewResumeDelay).ConfigureAwait(true);
        }
        catch
        {
            // Ignorar interrupções não previstas ao aguardar o reagendamento.
        }

        if (IsDisposed)
        {
            return;
        }

        ResumeMonitorPreviews();
    }

    private void ResumeMonitorPreviews()
    {
        foreach (var host in _monitorHosts.ToArray())
        {
            try
            {
                host.ResumeCapture();
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

    private sealed class MonitorCardContext
    {
        public MonitorCardContext(string monitorId, MonitorInfo monitor, Panel card, MonitorPreviewHost host)
        {
            MonitorId = monitorId ?? throw new ArgumentNullException(nameof(monitorId));
            Monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            Card = card ?? throw new ArgumentNullException(nameof(card));
            Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public string MonitorId { get; }

        public MonitorInfo Monitor { get; }

        public Panel Card { get; }

        public MonitorPreviewHost Host { get; }

        public MonitorTestForm? TestWindow { get; private set; }

        private bool _pausedForTestWindow;

        public void SetTestWindow(MonitorTestForm window)
        {
            ArgumentNullException.ThrowIfNull(window);

            if (ReferenceEquals(TestWindow, window))
            {
                return;
            }

            CloseTestWindow();

            TestWindow = window;
            TestWindow.FormClosed += OnTestWindowClosed;
            PauseHostForTestWindow();
        }

        public void CloseTestWindow()
        {
            if (TestWindow is not { IsDisposed: false } window)
            {
                TestWindow = null;
                ResumeHostFromTestWindow();
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
                ResumeHostFromTestWindow();
            }
        }

        private void OnTestWindowClosed(object? sender, FormClosedEventArgs e)
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

            ResumeHostFromTestWindow();
        }

        private void PauseHostForTestWindow()
        {
            if (_pausedForTestWindow)
            {
                return;
            }

            try
            {
                Host.Pause();
                _pausedForTestWindow = true;
            }
            catch
            {
                _pausedForTestWindow = false;
            }
        }

        private void ResumeHostFromTestWindow()
        {
            if (!_pausedForTestWindow)
            {
                return;
            }

            _pausedForTestWindow = false;

            try
            {
                Host.Resume();
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
        public void Info(string message, Exception? exception = null)
        {
            Debug.WriteLine($"[INFO] {message} {exception?.Message}");
        }

        public void Warn(string message, Exception? exception = null)
        {
            Debug.WriteLine($"[WARN] {message} {exception?.Message}");
        }

        public void Error(string message, Exception? exception = null)
        {
            Debug.WriteLine($"[ERROR] {message} {exception?.Message}");
        }
    }
}
