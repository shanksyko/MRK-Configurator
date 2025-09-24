#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Ui;
using Mieruka.App.Services;
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
    private bool _busy;
    private readonly List<MonitorInfo> _monitorSnapshot = new();
    private readonly List<MonitorCardContext> _monitorCardOrder = new();
    private readonly IMonitorService _monitorService = new MonitorService();
    private readonly List<MonitorPreviewHost> _monitorHosts = new();
    private readonly Dictionary<string, MonitorCardContext> _monitorCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _manuallyStoppedMonitors = new(StringComparer.OrdinalIgnoreCase);
    private IDisplayService? _displayService;
    private bool _previewsRequested;

    public string? SelectedMonitorId { get; private set; }

    public event EventHandler<string>? MonitorSelected;

    public MainForm()
    {
        InitializeComponent();

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

        LoadInitialData();
        UpdateButtonStates();
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
                    host.Start(preferGpu: true);
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
            Scale = 1.0,
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
                IsPrimary = screen.Primary,
                Scale = 1.0,
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

        _manuallyStoppedMonitors.Remove(monitorId);

        if (_monitorCards.TryGetValue(monitorId, out var context))
        {
            context.Host.Start(preferGpu: true);
        }

        UpdateSelectedMonitor(monitorId);
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
            var preview = new PreviewForm(new[] { context.Monitor });
            preview.Show(this);
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

    private void UpdateSelectedMonitor(string? monitorId, bool notify = true)
    {
        if (string.Equals(SelectedMonitorId, monitorId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateMonitorSelectionVisuals();
            return;
        }

        SelectedMonitorId = monitorId;
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

            context.Card.BackColor = isSelected
                ? System.Drawing.SystemColors.GradientInactiveCaption
                : System.Drawing.SystemColors.Control;
        }
    }

    private void StartAutomaticPreviews()
    {
        _previewsRequested = true;

        if (WindowState == FormWindowState.Minimized)
        {
            return;
        }

        foreach (var context in _monitorCardOrder)
        {
            if (_manuallyStoppedMonitors.Contains(context.MonitorId))
            {
                continue;
            }

            context.Host.Start(preferGpu: true);
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

        using var editor = new AppEditorForm(selected, monitors, SelectedMonitorId);
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
