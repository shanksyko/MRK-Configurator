using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;
using Mieruka.App.Config;
using Mieruka.App.Controls;
using Mieruka.App.Services;
using Mieruka.App.Services.Ui;
using Mieruka.App.Ui;
using Mieruka.Automation.Login;
using Mieruka.Automation.Tabs;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using OpenQA.Selenium;
using Serilog;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App;

/// <summary>
/// Main window used to assign applications and sites to monitors while exposing
/// validation feedback about the configuration.
/// </summary>
internal sealed class ConfigForm : WinForms.Form
{
    private readonly ConfiguratorWorkspace _workspace;
    private readonly JsonStore<GeneralConfig> _store;
    private readonly ConfigMigrator _migrator;
    private readonly IDisplayService? _displayService;
    private readonly ConfigValidator _validator;
    private readonly WinForms.ImageList _imageList;
    private readonly WinForms.ImageList _issueImageList;
    private readonly WinForms.ListView _applicationsList;
    private readonly WinForms.ListView _sitesList;
    private readonly BindingSource _appsBinding;
    private readonly BindingSource _sitesBinding;
    private WinForms.Button? _appAddButton;
    private WinForms.Button? _appEditButton;
    private WinForms.Button? _appRemoveButton;
    private WinForms.Button? _appDuplicateButton;
    private WinForms.Button? _appTestButton;
    private WinForms.Button? _siteAddButton;
    private WinForms.Button? _siteEditButton;
    private WinForms.Button? _siteRemoveButton;
    private WinForms.Button? _siteDuplicateButton;
    private WinForms.Button? _siteTestButton;
    private readonly WinForms.FlowLayoutPanel _applicationButtonPanel;
    private readonly WinForms.FlowLayoutPanel _siteButtonPanel;
    private readonly WinForms.ListView _issuesList;
    private const double ContentSplitterRatio = 0.35;
    private const double LayoutSplitterRatio = 0.35;
    private static readonly Drawing.Size DefaultMinimumSizeLogical = new(960, 600);
    private const int ContentPanel1MinLogical = 120;
    private const int ContentPanel2MinLogical = 160;
    private const int LayoutPanel1MinLogical = 140;
    private const int LayoutPanel2MinLogical = 200;

    private readonly WinForms.FlowLayoutPanel _monitorPanel;
    private readonly WinForms.SplitContainer _layoutContainer;
    private readonly WinForms.SplitContainer _contentContainer;
    private readonly WinForms.StatusStrip _statusStrip;
    private readonly WinForms.ToolStripStatusLabel _statusLabel;
    private readonly List<MonitorPreviewControl> _monitorPreviews = new();
    private ConfigValidationReport _validationReport = ConfigValidationReport.Empty;

    private readonly ITelemetry _telemetry;
    private readonly MonitorSeeder _monitorSeeder;
    private readonly WinForms.ToolTip _toolTip;
    private readonly WinForms.FlowLayoutPanel _footerPanel;
    private MonitorPreviewControl? _activePreview;
    private string? _selectedMonitorStableId;
    private readonly List<MonitorSelectionBinding> _monitorSelectionBindings = new();

    private EntryReference? _selectedEntry;
    private bool _isUpdatingSelection;
    private readonly object _testGate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigForm"/> class.
    /// </summary>
    /// <param name="workspace">Workspace that exposes the configuration being edited.</param>
    /// <param name="store">Backing store used to persist changes.</param>
    /// <param name="displayService">Optional display service used to observe topology changes.</param>
    public ConfigForm(ConfiguratorWorkspace workspace, JsonStore<GeneralConfig> store, IDisplayService? displayService, ConfigMigrator migrator, ITelemetry telemetry)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _displayService = displayService;
        _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        _validator = new ConfigValidator();
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _monitorSeeder = new MonitorSeeder();

        InitializeComponent();
        // MIERUKA_FIX
        SetStyle(
            WinForms.ControlStyles.AllPaintingInWmPaint
            | WinForms.ControlStyles.OptimizedDoubleBuffer
            | WinForms.ControlStyles.UserPaint,
            true);
        UpdateStyles();
        DoubleBuffered = true;

        _toolTip = ToolTipTamer.Create();

        _footerPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new WinForms.Padding(8),
        };

        AutoScaleMode = WinForms.AutoScaleMode.Dpi;

        Text = "MRK Configurator";
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        var minimumWidth = LogicalToDeviceUnits(DefaultMinimumSizeLogical.Width);
        var minimumHeight = LogicalToDeviceUnits(DefaultMinimumSizeLogical.Height);
        MinimumSize = new Drawing.Size(minimumWidth, minimumHeight);

        var menuStrip = BuildMenu();

        _imageList = new WinForms.ImageList
        {
            ImageSize = new Drawing.Size(32, 32),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        _imageList.Images.Add("app", SystemIcons.Application);
        _imageList.Images.Add("site", SystemIcons.Information);

        _issueImageList = new WinForms.ImageList
        {
            ImageSize = new Drawing.Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        _issueImageList.Images.Add("error", SystemIcons.Error);
        _issueImageList.Images.Add("warning", SystemIcons.Warning);

        _applicationsList = CreateListView();
        _applicationsList.SmallImageList = _imageList;
        _applicationsList.SelectedIndexChanged += OnApplicationsSelectedIndexChanged;
        _applicationsList.ItemDrag += OnListItemDrag;
        ConfigureApplicationListView(_applicationsList);

        _sitesList = CreateListView();
        _sitesList.SmallImageList = _imageList;
        _sitesList.SelectedIndexChanged += OnSitesSelectedIndexChanged;
        _sitesList.ItemDrag += OnListItemDrag;
        ConfigureSiteListView(_sitesList);

        _appsBinding = new BindingSource { DataSource = _workspace.Applications };
        _sitesBinding = new BindingSource { DataSource = _workspace.Sites };
        _appsBinding.ListChanged += (_, _) => RefreshApplicationsList();
        _sitesBinding.ListChanged += (_, _) => RefreshSitesList();

        _applicationButtonPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new WinForms.Padding(0, 6, 0, 0),
        };

        _siteButtonPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new WinForms.Padding(0, 6, 0, 0),
        };

        var appsTab = new TabPage("Aplicativos")
        {
            Padding = new WinForms.Padding(4),
        };
        appsTab.Controls.Add(BuildApplicationTab());

        var sitesTab = new TabPage("Sites")
        {
            Padding = new WinForms.Padding(4),
        };
        sitesTab.Controls.Add(BuildSiteTab());

        var tabControl = new TabControl
        {
            Dock = WinForms.DockStyle.Fill,
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
        };
        tabControl.TabPages.Add(appsTab);
        tabControl.TabPages.Add(sitesTab);

        var monitorContainer = new WinForms.Panel
        {
            Dock = WinForms.DockStyle.Fill,
            Padding = new WinForms.Padding(8),
        };

        _monitorPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new WinForms.Padding(4),
        };

        monitorContainer.Controls.Add(_monitorPanel);

        _contentContainer = new WinForms.SplitContainer
        {
            Name = "ContentSplitContainer",
            Dock = WinForms.DockStyle.Fill,
        };

        _contentContainer.Panel1.Controls.Add(tabControl);
        _contentContainer.Panel2.Controls.Add(monitorContainer);

        _issuesList = new WinForms.ListView
        {
            Dock = WinForms.DockStyle.Fill,
            MultiSelect = false,
            HideSelection = false,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            SmallImageList = _issueImageList,
        };
        _issuesList.Columns.Add("Tipo", 100, HorizontalAlignment.Left);
        _issuesList.Columns.Add("Mensagem", 400, HorizontalAlignment.Left);

        var issuesLabel = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Top,
            Text = "Problemas detectados",
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
            Padding = new WinForms.Padding(0, 0, 0, 6),
        };

        var issuesPanel = new WinForms.Panel
        {
            Dock = WinForms.DockStyle.Fill,
            Padding = new WinForms.Padding(8),
        };

        issuesPanel.Controls.Add(_issuesList);
        issuesPanel.Controls.Add(issuesLabel);

        _layoutContainer = new WinForms.SplitContainer
        {
            Name = "LayoutSplitContainer",
            Dock = WinForms.DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };

        _layoutContainer.Panel1.Controls.Add(issuesPanel);
        _layoutContainer.Panel2.Controls.Add(_contentContainer);
        _layoutContainer.Panel1Collapsed = true;

        _statusLabel = new WinForms.ToolStripStatusLabel("Arraste um item para um monitor e selecione a área desejada.");
        _statusStrip = new WinForms.StatusStrip();
        _statusStrip.Items.Add(_statusLabel);

        var detectMonitorsButton = CreateFooterButton(
            "Detectar Monitores",
            "Executa uma nova detecção de monitores usando APIs GDI.",
            OnDetectMonitorsClicked);
        var saveButton = CreateFooterButton(
            "Salvar Configuração",
            "Salva imediatamente todas as alterações no arquivo de configuração.",
            OnSaveConfigurationClicked);
        var validateButton = CreateFooterButton(
            "Validar Configuração",
            "Roda o validador e destaca problemas encontrados na configuração.",
            OnValidateConfigurationClicked);
        var applyPresetButton = CreateFooterButton(
            "Aplicar Preset",
            "Seleciona e aplica um preset de zonas para o item atual.",
            OnApplyPresetClicked);
        var previewButton = CreateFooterButton(
            "Testar Preview",
            "Abre a janela de preview para validar a captura de tela.",
            OnTestPreviewClicked);
        var restoreButton = CreateFooterButton(
            "Restaurar Padrão",
            "Restaura os presets de zona padrão do aplicativo.",
            OnRestoreDefaultsClicked);
        var closeButton = CreateFooterButton(
            "Fechar",
            "Fecha a janela do configurador.",
            OnCloseRequested);

        _footerPanel.Controls.AddRange(new WinForms.Control[]
        {
            detectMonitorsButton,
            saveButton,
            validateButton,
            applyPresetButton,
            previewButton,
            restoreButton,
            closeButton,
        });

        ApplyScaledLayoutMetrics();

        LayoutGuards.WireSplitterGuards(_contentContainer, null);
        LayoutGuards.WireSplitterGuards(_layoutContainer, null);
        BuildFooterButtons();
        ApplyInitialSplitters();

        Controls.Add(_layoutContainer);
        Controls.Add(_footerPanel);
        Controls.Add(_statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        ToolTipTamer.Tame(this, null);

        PopulateLists();
        BuildMonitorPreviews();
        RefreshValidation();

        TabLayoutGuard.Attach(this);

        if (_displayService is not null)
        {
            _displayService.TopologyChanged += OnTopologyChanged;
        }
    }

    private WinForms.MenuStrip BuildMenu()
    {
        var menuStrip = new WinForms.MenuStrip
        {
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
        };

        var fileMenu = new WinForms.ToolStripMenuItem("Arquivo");

        var importItem = new WinForms.ToolStripMenuItem("Importar...");
        importItem.Click += OnImportConfiguration;

        var exportItem = new WinForms.ToolStripMenuItem("Exportar...");
        exportItem.Click += OnExportConfiguration;

        fileMenu.DropDownItems.Add(importItem);
        fileMenu.DropDownItems.Add(exportItem);

        menuStrip.Items.Add(fileMenu);
        return menuStrip;
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyScaledLayoutMetrics();
        ApplyInitialSplitters();
        UpdateStatus();
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScaledLayoutMetrics();
        ApplyInitialSplitters();
    }

    private async Task RunUiAsync(Func<Task> action)
    {
        try
        {
            Enabled = false;
            UseWaitCursor = true;

            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao executar ação da interface.");
            WinForms.MessageBox.Show(this, $"Não foi possível concluir a operação: {ex.Message}", "Erro", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            Enabled = true;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_displayService is not null)
            {
                _displayService.TopologyChanged -= OnTopologyChanged;
            }

            foreach (var preview in _monitorPreviews)
            {
                preview.EntryDropped -= OnMonitorEntryDropped;
                preview.SelectionApplied -= OnMonitorSelectionApplied;
                preview.MonitorSelected -= OnMonitorSelected;
            }

            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override async void OnFormClosing(WinForms.FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        await RunUiAsync(() => OnFormClosingAsync(e));
    }

    private async Task OnFormClosingAsync(WinForms.FormClosingEventArgs e)
    {
        try
        {
            await SaveConfigAsync().ConfigureAwait(true);
        }
        catch
        {
            e.Cancel = true;
            throw;
        }
    }

    private void OnImportConfiguration(object? sender, EventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Filter = "Configurações JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            Title = "Importar configuração",
        };

        if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var config = _migrator.ImportFromFile(dialog.FileName);
            IEnumerable<MonitorInfo>? monitors = null;
            if (_displayService is not null)
            {
                var snapshot = _displayService.Monitors();
                if (snapshot.Count > 0)
                {
                    monitors = snapshot;
                }
            }

            _workspace.ApplyConfiguration(config, monitors);
            _selectedEntry = null;
            PopulateLists();
            BuildMonitorPreviews();
            RefreshValidation();
            UpdateStatus($"Configuração importada de {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao importar configuração.");
            WinForms.MessageBox.Show(this, $"Não foi possível importar a configuração: {ex.Message}", "Erro", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void OnExportConfiguration(object? sender, EventArgs e)
    {
        using var dialog = new WinForms.SaveFileDialog
        {
            Filter = "Configurações JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            Title = "Exportar configuração",
            FileName = "appsettings.json",
        };

        if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var config = _workspace.BuildConfiguration();
            _migrator.ExportToFile(dialog.FileName, config);
            UpdateStatus($"Configuração exportada para {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao exportar configuração.");
            WinForms.MessageBox.Show(this, $"Não foi possível exportar a configuração: {ex.Message}", "Erro", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void OnDetectMonitorsClicked(object? sender, EventArgs e)
    {
        Log.Information("Detecção de monitores solicitada manualmente.");

        try
        {
            var enumerator = new GdiMonitorEnumerator();
            var probes = enumerator.Enumerate();

            if (probes.Count == 0)
            {
                Log.Warning("Nenhum monitor foi encontrado durante a detecção manual.");
                WinForms.MessageBox.Show(this, "Nenhum monitor foi detectado. Verifique as conexões e tente novamente.", "Detecção de Monitores", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            var config = _workspace.BuildConfiguration();
            var updated = _monitorSeeder.ApplySeeds(config, probes, resetPresets: false);

            _workspace.ApplyConfiguration(updated, updated.Monitors);
            BuildMonitorPreviews();
            RefreshValidation();

            UpdateStatus($"{probes.Count} monitor(es) detectado(s) e carregado(s).");
            Log.Information("Detecção manual atualizou a configuração com {MonitorCount} monitor(es).", probes.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao executar a detecção de monitores.");
            WinForms.MessageBox.Show(this, $"Não foi possível detectar monitores: {ex.Message}", "Erro", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private async void OnSaveConfigurationClicked(object? sender, EventArgs e)
    {
        await RunUiAsync(SaveConfigAsync);
    }

    private async Task SaveConfigAsync()
    {
        Log.Information("Salvamento de configuração iniciado.");

        var config = _workspace.BuildConfiguration();
        if (!TryValidateBeforeSave(config, out var validationMessage))
        {
            throw new InvalidOperationException(validationMessage);
        }

        var migrated = _migrator.Migrate(config);
        await _store.SaveAsync(migrated).ConfigureAwait(true);

        UpdateStatus("Configuração salva com sucesso.");
        Log.Information("Configuração salva.");
    }

    private bool TryValidateBeforeSave(GeneralConfig config, out string message)
    {
        foreach (var app in config.Applications)
        {
            if (string.IsNullOrWhiteSpace(app.Id))
            {
                message = "Existe um aplicativo sem nome. Informe um identificador antes de salvar.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(app.ExecutablePath) || !File.Exists(app.ExecutablePath))
            {
                message = $"O executável configurado para '{app.Id}' não foi encontrado.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(app.TargetMonitorStableId))
            {
                message = $"Selecione um monitor para o aplicativo '{app.Id}'.";
                return false;
            }
        }

        foreach (var site in config.Sites)
        {
            if (string.IsNullOrWhiteSpace(site.Id))
            {
                message = "Existe um site sem nome configurado. Ajuste o identificador antes de salvar.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(site.Url)
                || !Uri.TryCreate(site.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                message = $"Informe uma URL http/https válida para o site '{site.Id}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(site.TargetMonitorStableId))
            {
                message = $"Selecione um monitor para o site '{site.Id}'.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void OnValidateConfigurationClicked(object? sender, EventArgs e)
    {
        Log.Information("Validação manual da configuração iniciada.");
        RefreshValidation();

        var issueCount = _validationReport.Issues.Count;
        if (issueCount == 0)
        {
            UpdateStatus("Nenhum problema encontrado.");
            WinForms.MessageBox.Show(this, "Nenhum problema foi encontrado na configuração.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            Log.Information("Validação manual concluída sem problemas.");
        }
        else
        {
            UpdateStatus($"{issueCount} problema(s) encontrado(s).");
            WinForms.MessageBox.Show(this, $"Foram encontrados {issueCount} problema(s). Consulte a lista para mais detalhes.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            Log.Information("Validação manual encontrou {IssueCount} problema(s).", issueCount);
        }
    }

    private void OnApplyPresetClicked(object? sender, EventArgs e)
    {
        Log.Information("Aplicação de preset solicitada.");

        if (_selectedEntry is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um aplicativo ou site antes de aplicar um preset.", "Aplicar Preset", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        var config = _workspace.BuildConfiguration();
        if (config.ZonePresets.Count == 0)
        {
            WinForms.MessageBox.Show(this, "Nenhum preset de zonas está disponível. Utilize 'Restaurar Padrão' para recriá-los.", "Aplicar Preset", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new PresetSelectionDialog(config.ZonePresets);
        if (dialog.ShowDialog(this) != WinForms.DialogResult.OK)
        {
            Log.Information("Aplicação de preset cancelada pelo usuário.");
            return;
        }

        if (dialog.SelectedPreset is null || dialog.SelectedZone is null)
        {
            return;
        }

        ApplyPresetToSelection(dialog.SelectedPreset, dialog.SelectedZone);
    }

    private void OnTestPreviewClicked(object? sender, EventArgs e)
    {
        Log.Information("Teste de preview solicitado.");

        if (_workspace.Monitors.Count == 0)
        {
            WinForms.MessageBox.Show(this, "Nenhum monitor está configurado para exibição de preview.", "Preview", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var previewForm = new PreviewForm(_workspace.Monitors);
            previewForm.ShowDialog(this);
            UpdateStatus("Preview encerrado.");
            Log.Information("Janela de preview encerrada pelo usuário.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao abrir a janela de preview.");
            WinForms.MessageBox.Show(this, $"Não foi possível abrir o preview: {ex.Message}", "Erro", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void OnRestoreDefaultsClicked(object? sender, EventArgs e)
    {
        Log.Information("Restauração dos presets padrão solicitada.");

        try
        {
            var config = _workspace.BuildConfiguration();
            var restored = config with { ZonePresets = _monitorSeeder.CreateDefaultZonePresets() };
            _workspace.ApplyConfiguration(restored, restored.Monitors);
            BuildMonitorPreviews();
            RefreshValidation();
            UpdateStatus("Presets padrão restaurados.");
            Log.Information("Presets padrão restaurados com sucesso.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao restaurar os presets padrão.");
            WinForms.MessageBox.Show(this, $"Não foi possível restaurar os presets padrão: {ex.Message}", "Erro", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Log.Information("Encerrando ConfigForm a pedido do usuário.");
        Close();
    }

    private void RefreshValidation()
    {
        ConfigValidationReport report;

        try
        {
            var config = _workspace.BuildConfiguration();
            report = _validator.Validate(config, _workspace.Monitors);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao validar a configuração.");
            report = new ConfigValidationReport(new[]
            {
                new ConfigValidationIssue(ConfigValidationSeverity.Error, $"Falha ao validar a configuração: {ex.Message}"),
            });
        }

        _validationReport = report;

        _issuesList.BeginUpdate();
        _issuesList.Items.Clear();

        foreach (var issue in _validationReport.Issues)
        {
            var severityText = issue.Severity == ConfigValidationSeverity.Error ? "Erro" : "Aviso";
            var description = string.IsNullOrWhiteSpace(issue.Source) ? issue.Message : $"{issue.Source}: {issue.Message}";
            var item = new WinForms.ListViewItem(severityText)
            {
                ImageKey = issue.Severity == ConfigValidationSeverity.Error ? "error" : "warning",
                Tag = issue,
            };
            item.SubItems.Add(description);
            _issuesList.Items.Add(item);
        }

        if (_issuesList.Columns.Count >= 2)
        {
            _issuesList.AutoResizeColumn(0, ColumnHeaderAutoResizeStyle.HeaderSize);
            _issuesList.AutoResizeColumn(1, ColumnHeaderAutoResizeStyle.ColumnContent);
        }

        _issuesList.EndUpdate();

        var hasIssues = _issuesList.Items.Count > 0;
        _layoutContainer.Panel1Collapsed = !hasIssues;
        if (hasIssues)
        {
            var desiredHeight = Math.Max(140, Height / 4);
            var available = Math.Max(140, Height - 200);
            var target = Math.Min(desiredHeight, available);
            LayoutGuards.SafeApplySplitter(_layoutContainer, target);
        }
        else
        {
            LayoutGuards.SafeApplySplitter(_layoutContainer);
        }

        UpdateStatus();
    }

    private void PopulateLists()
    {
        RefreshApplicationsList();
        RefreshSitesList();
    }

    private void RefreshApplicationsList()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshApplicationsList));
            return;
        }

        var selected = _applicationsList.SelectedItems.Count > 0
            ? _applicationsList.SelectedItems[0].Tag as EntryReference
            : _selectedEntry?.Kind == EntryKind.Application ? _selectedEntry : null;

        _applicationsList.BeginUpdate();
        _applicationsList.Items.Clear();

        foreach (var app in _workspace.Applications)
        {
            var item = CreateApplicationItem(app);
            _applicationsList.Items.Add(item);
        }

        _applicationsList.EndUpdate();

        if (selected is not null)
        {
            var item = EntryReference.FindItem(_applicationsList, selected);
            if (item is not null)
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }
        }

        AutoSizeColumns(_applicationsList);
        UpdateApplicationButtons();
    }

    private void RefreshSitesList()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(RefreshSitesList));
            return;
        }

        var selected = _sitesList.SelectedItems.Count > 0
            ? _sitesList.SelectedItems[0].Tag as EntryReference
            : _selectedEntry?.Kind == EntryKind.Site ? _selectedEntry : null;

        _sitesList.BeginUpdate();
        _sitesList.Items.Clear();

        foreach (var site in _workspace.Sites)
        {
            var item = CreateSiteItem(site);
            _sitesList.Items.Add(item);
        }

        _sitesList.EndUpdate();

        if (selected is not null)
        {
            var item = EntryReference.FindItem(_sitesList, selected);
            if (item is not null)
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }
        }

        AutoSizeColumns(_sitesList);
        UpdateSiteButtons();
    }

    private WinForms.ListViewItem CreateApplicationItem(AppConfig app)
    {
        var entry = EntryReference.Create(EntryKind.Application, app.Id);
        var displayName = string.IsNullOrWhiteSpace(app.Window.Title) ? app.Id : app.Window.Title;
        var item = new WinForms.ListViewItem(displayName)
        {
            Tag = entry,
            ImageKey = "app",
            ToolTipText = app.ExecutablePath,
        };

        item.SubItems.Add(app.ExecutablePath);
        item.SubItems.Add(app.Arguments ?? string.Empty);
        item.SubItems.Add(DescribeWindow(app.Window));
        return item;
    }

    private WinForms.ListViewItem CreateSiteItem(SiteConfig site)
    {
        var entry = EntryReference.Create(EntryKind.Site, site.Id);
        var displayName = string.IsNullOrWhiteSpace(site.Window.Title) ? site.Id : site.Window.Title;
        var item = new WinForms.ListViewItem(displayName)
        {
            Tag = entry,
            ImageKey = "site",
            ToolTipText = site.Url,
        };

        item.SubItems.Add(site.Url);
        item.SubItems.Add(site.Browser.ToString());

        var profile = string.Join(", ", new[]
        {
            string.IsNullOrWhiteSpace(site.UserDataDirectory) ? null : site.UserDataDirectory,
            string.IsNullOrWhiteSpace(site.ProfileDirectory) ? null : site.ProfileDirectory,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        item.SubItems.Add(profile);

        var modeParts = new List<string>();
        if (site.AppMode)
        {
            modeParts.Add("App");
        }
        else
        {
            modeParts.Add("Janela");
        }

        if (site.KioskMode)
        {
            modeParts.Add("Kiosk");
        }

        item.SubItems.Add(string.Join(" + ", modeParts));
        item.SubItems.Add(site.Login is null ? "Não" : "Sim");
        item.SubItems.Add(DescribeWindow(site.Window));
        return item;
    }

    private static void AutoSizeColumns(WinForms.ListView listView)
    {
        if (listView.Columns.Count == 0)
        {
            return;
        }

        foreach (ColumnHeader column in listView.Columns)
        {
            column.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent);
            var headerWidth = TextRenderer.MeasureText(column.Text, listView.Font).Width + 24;
            if (column.Width < headerWidth)
            {
                column.Width = headerWidth;
            }
        }
    }

    private void BuildMonitorPreviews()
    {
        _monitorPanel.SuspendLayout();
        foreach (var preview in _monitorPreviews)
        {
            preview.EntryDropped -= OnMonitorEntryDropped;
            preview.SelectionApplied -= OnMonitorSelectionApplied;
            preview.MonitorSelected -= OnMonitorSelected;
            _toolTip.SetToolTip(preview, null);
            preview.Dispose();
        }

        _monitorPreviews.Clear();
        _monitorPanel.Controls.Clear();

        foreach (var monitor in _workspace.Monitors)
        {
            var preview = new MonitorPreviewControl
            {
                Width = 320,
                Height = 260,
                Margin = new WinForms.Padding(8),
            };

            preview.Monitor = monitor;
            preview.EntryDropped += OnMonitorEntryDropped;
            preview.SelectionApplied += OnMonitorSelectionApplied;
            preview.MonitorSelected += OnMonitorSelected;
            _toolTip.SetToolTip(preview, BuildMonitorToolTip(monitor));

            _monitorPreviews.Add(preview);
            _monitorPanel.Controls.Add(preview);
        }

        _monitorPanel.ResumeLayout();
        UpdateMonitorPreviews();
    }

    private static WinForms.ListView CreateListView()
    {
        return new WinForms.ListView
        {
            Dock = WinForms.DockStyle.Fill,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
    }

    private WinForms.Control BuildApplicationTab()
    {
        var container = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        container.RowStyles.Add(new WinForms.RowStyle(SizeType.Percent, 100f));
        container.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));

        _applicationsList.Dock = WinForms.DockStyle.Fill;
        container.Controls.Add(_applicationsList, 0, 0);

        container.Controls.Add(_applicationButtonPanel, 0, 1);
        return container;
    }

    private WinForms.Control BuildSiteTab()
    {
        var container = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        container.RowStyles.Add(new WinForms.RowStyle(SizeType.Percent, 100f));
        container.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));

        _sitesList.Dock = WinForms.DockStyle.Fill;
        container.Controls.Add(_sitesList, 0, 0);

        container.Controls.Add(_siteButtonPanel, 0, 1);
        return container;
    }

    private void BuildFooterButtons()
    {
        _applicationButtonPanel.SuspendLayout();
        _applicationButtonPanel.Controls.Clear();

        _appAddButton ??= CreateActionButton("Adicionar", OnAddApplicationClicked);
        _appEditButton ??= CreateActionButton("Editar", OnEditApplicationClicked, enabled: false);
        _appRemoveButton ??= CreateActionButton("Remover", OnRemoveApplicationClicked, enabled: false);
        _appDuplicateButton ??= CreateActionButton("Duplicar", OnDuplicateApplicationClicked, enabled: false);
        _appTestButton ??= CreateActionButton("Testar", OnTestApplicationClicked, enabled: false);

        _applicationButtonPanel.Controls.Add(_appAddButton!);
        _applicationButtonPanel.Controls.Add(_appEditButton!);
        _applicationButtonPanel.Controls.Add(_appRemoveButton!);
        _applicationButtonPanel.Controls.Add(_appDuplicateButton!);
        _applicationButtonPanel.Controls.Add(_appTestButton!);
        _applicationButtonPanel.ResumeLayout();

        _siteButtonPanel.SuspendLayout();
        _siteButtonPanel.Controls.Clear();

        _siteAddButton ??= CreateActionButton("Adicionar", OnAddSiteClicked);
        _siteEditButton ??= CreateActionButton("Editar", OnEditSiteClicked, enabled: false);
        _siteRemoveButton ??= CreateActionButton("Remover", OnRemoveSiteClicked, enabled: false);
        _siteDuplicateButton ??= CreateActionButton("Duplicar", OnDuplicateSiteClicked, enabled: false);
        _siteTestButton ??= CreateActionButton("Testar", OnTestSiteClicked, enabled: false);

        _siteButtonPanel.Controls.Add(_siteAddButton!);
        _siteButtonPanel.Controls.Add(_siteEditButton!);
        _siteButtonPanel.Controls.Add(_siteRemoveButton!);
        _siteButtonPanel.Controls.Add(_siteDuplicateButton!);
        _siteButtonPanel.Controls.Add(_siteTestButton!);
        _siteButtonPanel.ResumeLayout();
    }

    private WinForms.Button CreateActionButton(string text, EventHandler onClick, bool enabled = true)
    {
        var button = new WinForms.Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Enabled = enabled,
            Margin = new WinForms.Padding(0, 0, 6, 0),
        };
        button.Click += onClick;
        return button;
    }

    private static void ConfigureApplicationListView(WinForms.ListView listView)
    {
        listView.Columns.Clear();
        listView.Columns.Add("Nome", 180, HorizontalAlignment.Left);
        listView.Columns.Add("Executável", 260, HorizontalAlignment.Left);
        listView.Columns.Add("Argumentos", 220, HorizontalAlignment.Left);
        listView.Columns.Add("Destino", 220, HorizontalAlignment.Left);
    }

    private static void ConfigureSiteListView(WinForms.ListView listView)
    {
        listView.Columns.Clear();
        listView.Columns.Add("Nome", 180, HorizontalAlignment.Left);
        listView.Columns.Add("URL", 260, HorizontalAlignment.Left);
        listView.Columns.Add("Navegador", 100, HorizontalAlignment.Left);
        listView.Columns.Add("Perfil", 200, HorizontalAlignment.Left);
        listView.Columns.Add("Modo", 160, HorizontalAlignment.Left);
        listView.Columns.Add("Login", 120, HorizontalAlignment.Left);
        listView.Columns.Add("Destino", 220, HorizontalAlignment.Left);
    }

    private void OnApplicationsSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        try
        {
            _isUpdatingSelection = true;
            if (_applicationsList.SelectedItems.Count > 0)
            {
                _sitesList.SelectedItems.Clear();
                _selectedEntry = _applicationsList.SelectedItems[0].Tag as EntryReference;
                _appsBinding.Position = _applicationsList.SelectedItems[0].Index;
            }
            else if (_sitesList.SelectedItems.Count == 0)
            {
                _selectedEntry = null;
                _appsBinding.Position = -1;
            }

            UpdateMonitorPreviews();
            UpdateStatus();
            UpdateApplicationButtons();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void OnSitesSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        try
        {
            _isUpdatingSelection = true;
            if (_sitesList.SelectedItems.Count > 0)
            {
                _applicationsList.SelectedItems.Clear();
                _selectedEntry = _sitesList.SelectedItems[0].Tag as EntryReference;
                _sitesBinding.Position = _sitesList.SelectedItems[0].Index;
            }
            else if (_applicationsList.SelectedItems.Count == 0)
            {
                _selectedEntry = null;
                _sitesBinding.Position = -1;
            }

            UpdateMonitorPreviews();
            UpdateStatus();
            UpdateSiteButtons();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void OnListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not WinForms.ListViewItem item || item.Tag is not EntryReference entry)
        {
            return;
        }

        DoDragDrop(EntryReference.CreateDataObject(entry), DragDropEffects.Move);
    }

    private void OnMonitorEntryDropped(object? sender, MonitorPreviewControl.EntryDroppedEventArgs e)
    {
        if (!_workspace.TryAssignEntryToMonitor(e.Entry, e.Monitor))
        {
            WinForms.MessageBox.Show(this, "Não foi possível aplicar a configuração ao monitor selecionado.", "Aviso", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        _selectedEntry = e.Entry;
        if (sender is MonitorPreviewControl preview)
        {
            _activePreview = preview;
        }
        UpdateSelectedMonitor(ResolveMonitorStableId(e.Monitor), updatePreview: false);
        SelectEntryInList(e.Entry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"{e.Entry.Id} atribuído a {e.Monitor.Name}.");
    }

    private void OnMonitorSelectionApplied(object? sender, MonitorPreviewControl.SelectionAppliedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            UpdateStatus("Selecione um aplicativo ou site antes de desenhar uma área.");
            return;
        }

        if (!_workspace.TryApplySelection(_selectedEntry, e.Monitor, e.Selection))
        {
            WinForms.MessageBox.Show(this, "Não foi possível aplicar a seleção ao item atual.", "Aviso", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Área atualizada para {_selectedEntry.Id} em {e.Monitor.Name}.");
    }

    private void OnMonitorSelected(object? sender, EventArgs e)
    {
        if (sender is not MonitorPreviewControl preview || preview.Monitor is null)
        {
            return;
        }

        foreach (var candidate in _monitorPreviews)
        {
            candidate.IsSelected = ReferenceEquals(candidate, preview);
        }

        _activePreview = preview;
        var stableId = ResolveMonitorStableId(preview.Monitor);
        Log.Information("Preview selected monitor={StableId}", stableId);
        UpdateSelectedMonitor(stableId, updatePreview: false);

        if (_selectedEntry is null)
        {
            UpdateStatus($"Monitor selecionado: {preview.Monitor.Name}");
            return;
        }

        var window = _workspace.GetWindow(_selectedEntry);
        if (window is null)
        {
            return;
        }

        if (KeysEqual(window.Monitor, preview.Monitor.Key))
        {
            UpdateMonitorPreviews();
            UpdateStatus($"{_selectedEntry.Id} já está atribuído a {preview.Monitor.Name}.");
            return;
        }

        Drawing.Rectangle? selection;
        if (window.FullScreen)
        {
            selection = null;
        }
        else
        {
            var previewBounds = WindowPlacementHelper.GetMonitorBounds(preview.Monitor);
            var monitorWidth = Math.Max(1, previewBounds.Width > 0 ? previewBounds.Width : preview.Monitor.Width);
            var monitorHeight = Math.Max(1, previewBounds.Height > 0 ? previewBounds.Height : preview.Monitor.Height);

            selection = new Drawing.Rectangle(
                window.X ?? 0,
                window.Y ?? 0,
                window.Width ?? monitorWidth,
                window.Height ?? monitorHeight);
        }

        if (!_workspace.TryAssignEntryToMonitor(_selectedEntry, preview.Monitor, selection))
        {
            WinForms.MessageBox.Show(this, "Não foi possível atribuir o monitor selecionado ao item atual.", "Aviso", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        switch (_selectedEntry.Kind)
        {
            case EntryKind.Application:
                RefreshApplicationsList();
                break;
            case EntryKind.Site:
                RefreshSitesList();
                break;
        }

        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"{_selectedEntry.Id} atribuído a {preview.Monitor.Name}.");
    }

    private void OnTopologyChanged(object? sender, EventArgs e)
    {
        try
        {
            var monitors = _displayService?.Monitors() ?? Array.Empty<MonitorInfo>();
            if (monitors.Count == 0)
            {
                return;
            }

            _workspace.UpdateMonitors(monitors);
            BuildMonitorPreviews();
            RefreshValidation();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao atualizar monitores");
        }
    }

    private void UpdateMonitorPreviews()
    {
        WindowConfig? window = null;
        MonitorInfo? windowMonitor = null;

        if (_selectedEntry is not null)
        {
            window = _workspace.GetWindow(_selectedEntry);
            if (window is not null)
            {
                windowMonitor = _workspace.FindMonitor(window.Monitor);
            }
        }

        MonitorPreviewControl? active = null;
        MonitorInfo? activeMonitor = windowMonitor ?? _activePreview?.Monitor;

        foreach (var preview in _monitorPreviews)
        {
            var selMonitor = preview.Monitor;
            var matchesWindow = window is not null && selMonitor is not null && KeysEqual(window.Monitor, selMonitor.Key);

            preview.DisplayWindow(matchesWindow ? window : null);

            var shouldSelect = activeMonitor is not null && selMonitor is not null && KeysEqual(activeMonitor.Key, selMonitor.Key);
            preview.IsSelected = shouldSelect;

            if (shouldSelect)
            {
                active = preview;
            }

            _toolTip.SetToolTip(preview, selMonitor is not null ? BuildMonitorToolTip(selMonitor) : null);
        }

        if (active is null && _monitorPreviews.Count > 0)
        {
            _monitorPreviews[0].IsSelected = true;
            active = _monitorPreviews[0];
        }

        _activePreview = active;
        var stableId = active?.Monitor is { } monitor ? ResolveMonitorStableId(monitor) : null;
        UpdateSelectedMonitor(stableId, updatePreview: false);
    }

    private void SelectEntryInList(EntryReference entry)
    {
        try
        {
            _isUpdatingSelection = true;

            if (entry.Kind == EntryKind.Application)
            {
                var item = EntryReference.FindItem(_applicationsList, entry);
                if (item is not null)
                {
                    _sitesList.SelectedItems.Clear();
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                }
            }
            else
            {
                var item = EntryReference.FindItem(_sitesList, entry);
                if (item is not null)
                {
                    _applicationsList.SelectedItems.Clear();
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                }
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void UpdateStatus(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _statusLabel.Text = message;
            return;
        }

        if (_validationReport.HasErrors)
        {
            var count = _validationReport.ErrorCount;
            _statusLabel.Text = count == 1
                ? "Execução bloqueada: 1 erro na configuração."
                : $"Execução bloqueada: {count} erros na configuração.";
            return;
        }

        if (_selectedEntry is null)
        {
            if (_validationReport.WarningCount > 0)
            {
                _statusLabel.Text = _validationReport.WarningCount == 1
                    ? "1 aviso pendente na configuração."
                    : $"{_validationReport.WarningCount} avisos pendentes na configuração.";
            }
            else
            {
                _statusLabel.Text = "Selecione um item e arraste para um monitor para iniciar.";
            }
            return;
        }

        var window = _workspace.GetWindow(_selectedEntry);
        if (window is null)
        {
            _statusLabel.Text = $"Nenhuma configuração aplicada para {_selectedEntry.Id}.";
            return;
        }

        var monitor = _workspace.FindMonitor(window.Monitor);
        if (monitor is null)
        {
            _statusLabel.Text = $"{_selectedEntry.Id} ainda não possui um monitor atribuído.";
            return;
        }

        var rectangle = _workspace.GetSelectionRectangle(window, monitor);
        _statusLabel.Text = $"{_selectedEntry.Id}: {monitor.Name} ({rectangle.X},{rectangle.Y}) {rectangle.Width}x{rectangle.Height}.";
    }

    private void ApplyInitialSplitters()
    {
        ApplySplitterRatio(_layoutContainer, LayoutSplitterRatio);
        ApplySplitterRatio(_contentContainer, ContentSplitterRatio);
    }

    private WinForms.Button CreateFooterButton(string text, string toolTipText, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(toolTipText);
        ArgumentNullException.ThrowIfNull(handler);

        var font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        var button = new WinForms.Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new WinForms.Padding(4),
            Text = text,
            Font = font,
        };

        button.Click += handler;
        _toolTip.SetToolTip(button, toolTipText);
        return button;
    }

    private void ApplyScaledLayoutMetrics()
    {
        var minWidth = LogicalToDeviceUnits(DefaultMinimumSizeLogical.Width);
        var minHeight = LogicalToDeviceUnits(DefaultMinimumSizeLogical.Height);
        MinimumSize = new Drawing.Size(minWidth, minHeight);

        if (_contentContainer is not null)
        {
            var panel1Min = Math.Max(100, LogicalToDeviceUnits(ContentPanel1MinLogical));
            var panel2Min = Math.Max(100, LogicalToDeviceUnits(ContentPanel2MinLogical));
            LayoutGuards.SetSafeMinSizes(_contentContainer, panel1Min, panel2Min);
        }

        if (_layoutContainer is not null)
        {
            var panel1Min = Math.Max(100, LogicalToDeviceUnits(LayoutPanel1MinLogical));
            var panel2Min = Math.Max(100, LogicalToDeviceUnits(LayoutPanel2MinLogical));
            LayoutGuards.SetSafeMinSizes(_layoutContainer, panel1Min, panel2Min);
        }
    }

    private void ApplySplitterRatio(WinForms.SplitContainer? container, double ratio)
    {
        if (container is null || container.IsDisposed)
        {
            return;
        }

        ratio = Math.Clamp(ratio, 0d, 1d);

        if (!TryGetAvailableLength(container, out var length))
        {
            container.BeginInvoke(new Action(() => ApplySplitterRatio(container, ratio)));
            return;
        }

        var desired = (int)Math.Round(length * ratio);
        var before = container.SplitterDistance;

        LayoutGuards.SafeApplySplitter(container, desired);

        if (container.SplitterDistance != before)
        {
            var context = GetContainerContext(container);
            _telemetry.Info($"Splitter distance for {context} updated from {before} to {container.SplitterDistance}.");
        }
    }

    private static bool TryGetAvailableLength(WinForms.SplitContainer container, out int length)
    {
        length = container.Orientation == Orientation.Horizontal
            ? container.ClientSize.Height
            : container.ClientSize.Width;

        return length > 0;
    }

    private string GetContainerContext(WinForms.SplitContainer container)
    {
        if (ReferenceEquals(container, _layoutContainer))
        {
            return "layout split container";
        }

        if (ReferenceEquals(container, _contentContainer))
        {
            return "content split container";
        }

        if (!string.IsNullOrWhiteSpace(container.Name))
        {
            return container.Name;
        }

        var accessibleName = container.AccessibleName;
        if (!string.IsNullOrWhiteSpace(accessibleName))
        {
            return accessibleName;
        }

        return container.GetType().Name;
    }

    private void ApplyPresetToSelection(ZonePreset preset, ZonePreset.Zone zone)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(zone);

        if (_selectedEntry is null)
        {
            return;
        }

        var window = _workspace.GetWindow(_selectedEntry);
        if (window is null)
        {
            WinForms.MessageBox.Show(this, "O item selecionado ainda não possui uma janela configurada.", "Aplicar Preset", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        var monitor = _workspace.FindMonitor(window.Monitor);
        if (monitor is null)
        {
            WinForms.MessageBox.Show(this, "Associe o item a um monitor antes de aplicar um preset.", "Aplicar Preset", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        var rectangle = CalculateZoneRectangle(zone, monitor);
        if (!_workspace.TryAssignEntryToMonitor(_selectedEntry, monitor, rectangle))
        {
            WinForms.MessageBox.Show(this, "Não foi possível aplicar o preset ao item selecionado.", "Aplicar Preset", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Preset '{preset.Name}' aplicado ao item {_selectedEntry.Id}.");
        Log.Information(
            "Preset {PresetId} aplicado ao item {EntryId} no monitor {MonitorName}.",
            preset.Id,
            _selectedEntry.Id,
            monitor.Name);
    }

    private static Drawing.Rectangle CalculateZoneRectangle(ZonePreset.Zone zone, MonitorInfo monitor)
    {
        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
        var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

        var width = Math.Max(1, (int)Math.Round(monitorWidth * (zone.WidthPercentage / 100d)));
        var height = Math.Max(1, (int)Math.Round(monitorHeight * (zone.HeightPercentage / 100d)));
        var x = (int)Math.Round(monitorWidth * (zone.LeftPercentage / 100d));
        var y = (int)Math.Round(monitorHeight * (zone.TopPercentage / 100d));

        switch (zone.Anchor)
        {
            case ZoneAnchor.TopCenter:
                x -= width / 2;
                break;
            case ZoneAnchor.TopRight:
                x -= width;
                break;
            case ZoneAnchor.CenterLeft:
                y -= height / 2;
                break;
            case ZoneAnchor.Center:
                x -= width / 2;
                y -= height / 2;
                break;
            case ZoneAnchor.CenterRight:
                x -= width;
                y -= height / 2;
                break;
            case ZoneAnchor.BottomLeft:
                y -= height;
                break;
            case ZoneAnchor.BottomCenter:
                x -= width / 2;
                y -= height;
                break;
            case ZoneAnchor.BottomRight:
                x -= width;
                y -= height;
                break;
            case ZoneAnchor.TopLeft:
            default:
                break;
        }

        x = Math.Clamp(x, 0, Math.Max(0, monitorWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, monitorHeight - height));

        return new Drawing.Rectangle(x, y, width, height);
    }

    private static bool KeysEqual(MonitorKey? left, MonitorKey? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }

    private string DescribeWindow(WindowConfig window)
    {
        var monitors = _workspace.Monitors;
        if (monitors.Count == 0)
        {
            return "Sem monitor";
        }

        var monitor = WindowPlacementHelper.ResolveMonitor(_displayService, monitors, window);
        var monitorName = !string.IsNullOrWhiteSpace(monitor.Name)
            ? monitor.Name
            : !string.IsNullOrWhiteSpace(monitor.DeviceName)
                ? monitor.DeviceName
                : $"Monitor {monitor.Key.DisplayIndex + 1}";

        string zone;
        if (window.FullScreen)
        {
            zone = "Tela cheia";
        }
        else
        {
            var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
            var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
            var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);
            var width = window.Width ?? monitorWidth;
            var height = window.Height ?? monitorHeight;
            var x = window.X ?? 0;
            var y = window.Y ?? 0;
            zone = $"{width}x{height} @ ({x},{y})";
        }

        return $"{monitorName} - {zone}";
    }

    private WindowConfig NormalizeWindow(WindowConfig window)
    {
        var monitors = _workspace.Monitors;
        if (monitors.Count == 0)
        {
            return window with
            {
                Monitor = new MonitorKey(),
                X = null,
                Y = null,
                Width = null,
                Height = null,
                FullScreen = true,
            };
        }

        var monitor = WindowPlacementHelper.ResolveMonitor(_displayService, monitors, window);
        if (!monitors.Any(m => KeysEqual(m.Key, monitor.Key)))
        {
            monitor = monitors[0];
        }

        if (window.FullScreen)
        {
            return window with
            {
                Monitor = monitor.Key,
                X = null,
                Y = null,
                Width = null,
                Height = null,
                FullScreen = true,
            };
        }

        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
        var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

        var width = Math.Clamp(window.Width ?? monitorWidth, 1, monitorWidth);
        var height = Math.Clamp(window.Height ?? monitorHeight, 1, monitorHeight);
        var x = Math.Clamp(window.X ?? 0, 0, Math.Max(0, monitorWidth - width));
        var y = Math.Clamp(window.Y ?? 0, 0, Math.Max(0, monitorHeight - height));

        return window with
        {
            Monitor = monitor.Key,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            FullScreen = false,
        };
    }

    private string EnsureUniqueApplicationId(string candidate, string? originalId = null)
    {
        var baseId = string.IsNullOrWhiteSpace(candidate) ? "Aplicativo" : candidate.Trim();
        var unique = baseId;
        var suffix = 1;

        bool Exists(string value) => _workspace.Applications.Any(app =>
            !string.Equals(app.Id, originalId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(app.Id, value, StringComparison.OrdinalIgnoreCase));

        while (Exists(unique))
        {
            suffix++;
            unique = $"{baseId} ({suffix})";
        }

        return unique;
    }

    private string EnsureUniqueSiteId(string candidate, string? originalId = null)
    {
        var baseId = string.IsNullOrWhiteSpace(candidate) ? "Site" : candidate.Trim();
        var unique = baseId;
        var suffix = 1;

        bool Exists(string value) => _workspace.Sites.Any(site =>
            !string.Equals(site.Id, originalId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(site.Id, value, StringComparison.OrdinalIgnoreCase));

        while (Exists(unique))
        {
            suffix++;
            unique = $"{baseId} ({suffix})";
        }

        return unique;
    }

    private void UpdateApplicationButtons()
    {
        if (_appEditButton is null || _appRemoveButton is null || _appDuplicateButton is null || _appTestButton is null)
        {
            return;
        }

        var hasSelection = _applicationsList.SelectedItems.Count > 0;
        _appEditButton.Enabled = hasSelection;
        _appRemoveButton.Enabled = hasSelection;
        _appDuplicateButton.Enabled = hasSelection;
        _appTestButton.Enabled = hasSelection && OperatingSystem.IsWindows();
    }

    private void UpdateSiteButtons()
    {
        if (_siteEditButton is null || _siteRemoveButton is null || _siteDuplicateButton is null || _siteTestButton is null)
        {
            return;
        }

        var hasSelection = _sitesList.SelectedItems.Count > 0;
        _siteEditButton.Enabled = hasSelection;
        _siteRemoveButton.Enabled = hasSelection;
        _siteDuplicateButton.Enabled = hasSelection;
        _siteTestButton.Enabled = hasSelection && OperatingSystem.IsWindows();
    }

    private AppConfig? GetSelectedApplication()
    {
        if (_applicationsList.SelectedItems.Count == 0)
        {
            return null;
        }

        if (_applicationsList.SelectedItems[0].Tag is not EntryReference entry)
        {
            return null;
        }

        return _workspace.Applications.FirstOrDefault(app =>
            string.Equals(app.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
    }

    private SiteConfig? GetSelectedSite()
    {
        if (_sitesList.SelectedItems.Count == 0)
        {
            return null;
        }

        if (_sitesList.SelectedItems[0].Tag is not EntryReference entry)
        {
            return null;
        }

        return _workspace.Sites.FirstOrDefault(site =>
            string.Equals(site.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyApplicationUpdate(string originalId, AppConfig updated)
    {
        for (var i = 0; i < _workspace.Applications.Count; i++)
        {
            if (string.Equals(_workspace.Applications[i].Id, originalId, StringComparison.OrdinalIgnoreCase))
            {
                _workspace.Applications[i] = updated;
                return;
            }
        }
    }

    private void ApplySiteUpdate(string originalId, SiteConfig updated)
    {
        for (var i = 0; i < _workspace.Sites.Count; i++)
        {
            if (string.Equals(_workspace.Sites[i].Id, originalId, StringComparison.OrdinalIgnoreCase))
            {
                _workspace.Sites[i] = updated;
                return;
            }
        }
    }

    private static string DescribeZone(WindowConfig window)
    {
        if (window.FullScreen)
        {
            return "full";
        }

        var width = window.Width ?? 0;
        var height = window.Height ?? 0;
        var x = window.X ?? 0;
        var y = window.Y ?? 0;
        return $"{width}x{height}@{x},{y}";
    }

    private static string DescribeZone(AppConfig app)
        => !string.IsNullOrWhiteSpace(app.TargetZonePresetId)
            ? app.TargetZonePresetId!
            : DescribeZone(app.Window);

    private static string DescribeZone(SiteConfig site)
        => !string.IsNullOrWhiteSpace(site.TargetZonePresetId)
            ? site.TargetZonePresetId!
            : DescribeZone(site.Window);

    private static string ResolveMonitorStableId(MonitorInfo monitor)
        => WindowPlacementHelper.ResolveStableId(monitor);

    private static string BuildMonitorToolTip(MonitorInfo monitor)
    {
        var lines = new List<string>
        {
            $"ID: {ResolveMonitorStableId(monitor)}",
            $"Resolução: {monitor.Width}x{monitor.Height}",
            $"Escala: {(monitor.Scale > 0 ? monitor.Scale : 1):P0}",
        };

        if (!string.IsNullOrWhiteSpace(monitor.DeviceName))
        {
            lines.Add($"Device: {monitor.DeviceName}");
        }

        if (!string.IsNullOrWhiteSpace(monitor.Connector))
        {
            lines.Add($"Conector: {monitor.Connector}");
        }

        lines.Add(monitor.IsPrimary ? "Principal: Sim" : "Principal: Não");
        return string.Join(Environment.NewLine, lines);
    }

    private void UpdateSelectedMonitor(string? stableId, bool updatePreview = true)
    {
        var normalized = string.IsNullOrWhiteSpace(stableId) ? null : stableId;
        if (string.Equals(_selectedMonitorStableId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            ApplyMonitorSelectionToBindings(normalized);
            return;
        }

        _selectedMonitorStableId = normalized;

        if (updatePreview)
        {
            HighlightMonitorByStableId(normalized);
        }

        ApplyMonitorSelectionToBindings(normalized);
    }

    private void HighlightMonitorByStableId(string? stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return;
        }

        MonitorPreviewControl? matched = null;

        foreach (var preview in _monitorPreviews)
        {
            var monitor = preview.Monitor;
            if (monitor is null)
            {
                preview.IsSelected = false;
                continue;
            }

            var isMatch = string.Equals(ResolveMonitorStableId(monitor), stableId, StringComparison.OrdinalIgnoreCase);
            preview.IsSelected = isMatch;
            if (isMatch)
            {
                matched = preview;
            }
        }

        if (matched is not null)
        {
            _activePreview = matched;
        }
    }

    private void ApplyMonitorSelectionToBindings(string? stableId)
    {
        for (var i = _monitorSelectionBindings.Count - 1; i >= 0; i--)
        {
            var binding = _monitorSelectionBindings[i];
            if (binding.Dialog.IsDisposed)
            {
                _monitorSelectionBindings.RemoveAt(i);
                continue;
            }

            binding.Apply(stableId);
        }
    }

    private void RegisterMonitorBinding(WinForms.Form dialog, Action<string?> applySelection)
    {
        if (dialog is null || applySelection is null)
        {
            return;
        }

        _monitorSelectionBindings.Add(new MonitorSelectionBinding(dialog, applySelection));
    }

    private void UnregisterMonitorBinding(WinForms.Form dialog)
    {
        for (var i = _monitorSelectionBindings.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_monitorSelectionBindings[i].Dialog, dialog))
            {
                _monitorSelectionBindings.RemoveAt(i);
            }
        }
    }

    private void OnDialogMonitorSelectionChanged(object? sender, string? stableId)
        => UpdateSelectedMonitor(stableId);

    private sealed record class MonitorSelectionBinding(WinForms.Form Dialog, Action<string?> Apply);


    private void OnAddApplicationClicked(object? sender, EventArgs e)
    {
        using var dialog = new AppEditorDialog(
            _workspace.Monitors,
            _workspace.ZonePresets.ToList(),
            template: null,
            selectedMonitorStableId: _selectedMonitorStableId);
        dialog.TestHandler = config => RunApplicationTestAsync(config, dialog, updateStatus: false);
        RegisterMonitorBinding(dialog, dialog.SetSelectedMonitorStableId);
        dialog.MonitorSelectionChanged += OnDialogMonitorSelectionChanged;
        dialog.SetSelectedMonitorStableId(_selectedMonitorStableId);

        var result = dialog.ShowDialog(this);
        dialog.MonitorSelectionChanged -= OnDialogMonitorSelectionChanged;
        UnregisterMonitorBinding(dialog);

        if (result != WinForms.DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        var window = NormalizeWindow(dialog.Result.Window);
        var id = EnsureUniqueApplicationId(dialog.Result.Id);
        var app = dialog.Result with { Id = id, Window = window };

        UpdateSelectedMonitor(app.TargetMonitorStableId);
        _selectedEntry = EntryReference.Create(EntryKind.Application, app.Id);
        _workspace.Applications.Add(app);
        Log.Information("Apps:Add -> {Name}", app.Id);

        RefreshApplicationsList();
        SelectEntryInList(_selectedEntry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Aplicativo '{app.Id}' adicionado.");
    }

    private void OnEditApplicationClicked(object? sender, EventArgs e)
    {
        var current = GetSelectedApplication();
        if (current is null)
        {
            return;
        }

        using var dialog = new AppEditorDialog(
            _workspace.Monitors,
            _workspace.ZonePresets.ToList(),
            current,
            _selectedMonitorStableId);
        dialog.TestHandler = config => RunApplicationTestAsync(config, dialog, updateStatus: false);
        RegisterMonitorBinding(dialog, dialog.SetSelectedMonitorStableId);
        dialog.MonitorSelectionChanged += OnDialogMonitorSelectionChanged;
        dialog.SetSelectedMonitorStableId(_selectedMonitorStableId);

        var result = dialog.ShowDialog(this);
        dialog.MonitorSelectionChanged -= OnDialogMonitorSelectionChanged;
        UnregisterMonitorBinding(dialog);

        if (result != WinForms.DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        var window = NormalizeWindow(dialog.Result.Window);
        var id = EnsureUniqueApplicationId(dialog.Result.Id, current.Id);
        var updated = dialog.Result with { Id = id, Window = window };

        UpdateSelectedMonitor(updated.TargetMonitorStableId);
        ApplyApplicationUpdate(current.Id, updated);
        _selectedEntry = EntryReference.Create(EntryKind.Application, updated.Id);
        Log.Information("Apps:Edit -> {Name}", updated.Id);

        RefreshApplicationsList();
        SelectEntryInList(_selectedEntry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Aplicativo '{updated.Id}' atualizado.");
    }

    private void OnRemoveApplicationClicked(object? sender, EventArgs e)
    {
        var current = GetSelectedApplication();
        if (current is null)
        {
            return;
        }

        if (WinForms.MessageBox.Show(this, $"Deseja remover o aplicativo '{current.Id}'?", "Remover aplicativo", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question) != WinForms.DialogResult.Yes)
        {
            return;
        }

        Log.Information("Apps:Remove -> {Name}", current.Id);
        _workspace.Applications.Remove(current);
        _selectedEntry = null;

        RefreshApplicationsList();
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Aplicativo '{current.Id}' removido.");
    }

    private void OnDuplicateApplicationClicked(object? sender, EventArgs e)
    {
        var current = GetSelectedApplication();
        if (current is null)
        {
            return;
        }

        var candidateId = $"{current.Id} (cópia)";
        var id = EnsureUniqueApplicationId(candidateId);
        var title = string.IsNullOrWhiteSpace(current.Window.Title) ? id : $"{current.Window.Title} (cópia)";
        var window = NormalizeWindow(current.Window with { Title = title });

        var duplicate = current with
        {
            Id = id,
            Window = window,
        };

        UpdateSelectedMonitor(duplicate.TargetMonitorStableId);
        _selectedEntry = EntryReference.Create(EntryKind.Application, duplicate.Id);
        _workspace.Applications.Add(duplicate);
        Log.Information("Apps:Duplicate -> {Name}", duplicate.Id);

        RefreshApplicationsList();
        SelectEntryInList(_selectedEntry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Aplicativo '{duplicate.Id}' duplicado.");
    }

    private async void OnTestApplicationClicked(object? sender, EventArgs e)
    {
        var app = GetSelectedApplication();
        if (app is null)
        {
            return;
        }

        if (_appTestButton is not null)
        {
            _appTestButton.Enabled = false;
        }

        try
        {
            await RunApplicationTestAsync(app, this, updateStatus: true).ConfigureAwait(true);
        }
        finally
        {
            UpdateApplicationButtons();
        }
    }

    private async Task<bool> RunApplicationTestAsync(AppConfig app, WinForms.Control owner, bool updateStatus)
    {
        if (updateStatus)
        {
            UpdateStatus($"Testando aplicativo '{app.Id}'...");
        }

        Log.Information("Apps:Test -> {Name}", app.Id);
        var success = await Task.Run(() => TestApplication(app, owner)).ConfigureAwait(true);

        var monitor = ResolveTargetMonitor(app);
        var result = success ? "ok" : "fail";
        Log.Information(
            "Test(App) -> monitor={Monitor}, zone={Zone}, result={Result}",
            ResolveMonitorStableId(monitor),
            DescribeZone(app),
            result);

        if (updateStatus)
        {
            UpdateStatus(success
                ? $"Aplicativo '{app.Id}' iniciado com sucesso."
                : $"Falha ao testar '{app.Id}'. Verifique o log para mais detalhes.");
        }

        return success;
    }

    private bool TestApplication(AppConfig app, WinForms.Control owner)
    {
        if (!File.Exists(app.ExecutablePath))
        {
            owner.BeginInvoke(new Action(() =>
                WinForms.MessageBox.Show(owner, $"O executável '{app.ExecutablePath}' não foi encontrado.", "Teste de aplicativo", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning)));
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = app.ExecutablePath,
                Arguments = app.Arguments ?? string.Empty,
                UseShellExecute = false,
            };

            foreach (var pair in app.EnvironmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                owner.BeginInvoke(new Action(() =>
                    WinForms.MessageBox.Show(owner, "Process.Start retornou nulo ao iniciar o aplicativo.", "Teste de aplicativo", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error)));
                return false;
            }

            var monitor = ResolveTargetMonitor(app);
            var zoneRect = ResolveZoneRect(monitor, app.TargetZonePresetId, app.Window);
            var topMost = app.Window.AlwaysOnTop || (zoneRect.WidthPercentage >= 99.5 && zoneRect.HeightPercentage >= 99.5);

            WindowPlacementHelper.ForcePlaceProcessWindowAsync(
                process,
                monitor,
                zoneRect,
                topMost,
                CancellationToken.None).GetAwaiter().GetResult();

            process.Refresh();
            if (process.MainWindowHandle == IntPtr.Zero)
            {
                owner.BeginInvoke(new Action(() =>
                    WinForms.MessageBox.Show(owner, "O aplicativo foi iniciado, mas a janela principal não foi localizada.", "Teste de aplicativo", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning)));
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao testar aplicativo {AppId}.", app.Id);
            owner.BeginInvoke(new Action(() =>
                WinForms.MessageBox.Show(owner, $"Falha ao iniciar o aplicativo: {ex.Message}", "Teste de aplicativo", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error)));
            return false;
        }
    }

    private MonitorInfo ResolveTargetMonitor(AppConfig app)
        => WindowPlacementHelper.ResolveTargetMonitor(
            app,
            _selectedMonitorStableId,
            _workspace.Monitors,
            _displayService);

    private MonitorInfo ResolveTargetMonitor(SiteConfig site)
        => WindowPlacementHelper.ResolveTargetMonitor(
            site,
            _selectedMonitorStableId,
            _workspace.Monitors,
            _displayService);

    private WindowPlacementHelper.ZoneRect ResolveZoneRect(MonitorInfo monitor, string? zoneIdentifier, WindowConfig window)
        => WindowPlacementHelper.ResolveTargetZone(monitor, zoneIdentifier, window, _workspace.ZonePresets);

    private bool TryPositionWindow(AppConfig app, IntPtr handle)
    {
        var monitor = ResolveTargetMonitor(app);
        return TryPositionWindowInternal(monitor, app.TargetZonePresetId, app.Window, handle);
    }

    private bool TryPositionWindow(SiteConfig site, IntPtr handle)
    {
        var monitor = ResolveTargetMonitor(site);
        return TryPositionWindowInternal(monitor, site.TargetZonePresetId, site.Window, handle);
    }

    private bool TryPositionWindowInternal(MonitorInfo monitor, string? zoneIdentifier, WindowConfig window, IntPtr handle)
    {
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var zoneRect = ResolveZoneRect(monitor, zoneIdentifier, window);
            var topMost = window.AlwaysOnTop || (zoneRect.WidthPercentage >= 99.5 && zoneRect.HeightPercentage >= 99.5);
            return WindowPlacementHelper.PlaceWindow(handle, monitor, zoneRect, topMost);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha ao posicionar janela durante o teste.");
            return false;
        }
    }


    private void OnAddSiteClicked(object? sender, EventArgs e)
    {
        using var dialog = new SiteEditorDialog(
            _workspace.Monitors,
            _workspace.ZonePresets.ToList(),
            template: null,
            selectedMonitorStableId: _selectedMonitorStableId);
        dialog.TestHandler = config => RunSiteTestAsync(config, dialog, updateStatus: false);
        RegisterMonitorBinding(dialog, dialog.SetSelectedMonitorStableId);
        dialog.MonitorSelectionChanged += OnDialogMonitorSelectionChanged;
        dialog.SetSelectedMonitorStableId(_selectedMonitorStableId);

        var result = dialog.ShowDialog(this);
        dialog.MonitorSelectionChanged -= OnDialogMonitorSelectionChanged;
        UnregisterMonitorBinding(dialog);

        if (result != WinForms.DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        var window = NormalizeWindow(dialog.Result.Window);
        var id = EnsureUniqueSiteId(dialog.Result.Id);
        var site = dialog.Result with
        {
            Id = id,
            Window = window,
            BrowserArguments = dialog.Result.BrowserArguments?.ToList() ?? new List<string>(),
            AllowedTabHosts = dialog.Result.AllowedTabHosts?.ToList() ?? new List<string>(),
        };

        UpdateSelectedMonitor(site.TargetMonitorStableId);
        _selectedEntry = EntryReference.Create(EntryKind.Site, site.Id);
        _workspace.Sites.Add(site);
        Log.Information("Sites:Add -> {Name}", $"{site.Id}|{site.Url}");

        RefreshSitesList();
        SelectEntryInList(_selectedEntry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Site '{site.Id}' adicionado.");
    }

    private void OnEditSiteClicked(object? sender, EventArgs e)
    {
        var current = GetSelectedSite();
        if (current is null)
        {
            return;
        }

        using var dialog = new SiteEditorDialog(
            _workspace.Monitors,
            _workspace.ZonePresets.ToList(),
            current,
            _selectedMonitorStableId);
        dialog.TestHandler = config => RunSiteTestAsync(config, dialog, updateStatus: false);
        RegisterMonitorBinding(dialog, dialog.SetSelectedMonitorStableId);
        dialog.MonitorSelectionChanged += OnDialogMonitorSelectionChanged;
        dialog.SetSelectedMonitorStableId(_selectedMonitorStableId);

        var result = dialog.ShowDialog(this);
        dialog.MonitorSelectionChanged -= OnDialogMonitorSelectionChanged;
        UnregisterMonitorBinding(dialog);

        if (result != WinForms.DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        var window = NormalizeWindow(dialog.Result.Window);
        var id = EnsureUniqueSiteId(dialog.Result.Id, current.Id);
        var updated = dialog.Result with
        {
            Id = id,
            Window = window,
            BrowserArguments = dialog.Result.BrowserArguments?.ToList() ?? new List<string>(),
            AllowedTabHosts = dialog.Result.AllowedTabHosts?.ToList() ?? new List<string>(),
        };

        UpdateSelectedMonitor(updated.TargetMonitorStableId);
        ApplySiteUpdate(current.Id, updated);
        _selectedEntry = EntryReference.Create(EntryKind.Site, updated.Id);
        Log.Information("Sites:Edit -> {Name}", $"{updated.Id}|{updated.Url}");

        RefreshSitesList();
        SelectEntryInList(_selectedEntry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Site '{updated.Id}' atualizado.");
    }

    private void OnRemoveSiteClicked(object? sender, EventArgs e)
    {
        var current = GetSelectedSite();
        if (current is null)
        {
            return;
        }

        if (WinForms.MessageBox.Show(this, $"Deseja remover o site '{current.Id}'?", "Remover site", WinForms.MessageBoxButtons.YesNo, WinForms.MessageBoxIcon.Question) != WinForms.DialogResult.Yes)
        {
            return;
        }

        Log.Information("Sites:Remove -> {Name}", $"{current.Id}|{current.Url}");
        _workspace.Sites.Remove(current);
        _selectedEntry = null;

        RefreshSitesList();
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Site '{current.Id}' removido.");
    }

    private void OnDuplicateSiteClicked(object? sender, EventArgs e)
    {
        var current = GetSelectedSite();
        if (current is null)
        {
            return;
        }

        var candidateId = $"{current.Id} (cópia)";
        var id = EnsureUniqueSiteId(candidateId);
        var title = string.IsNullOrWhiteSpace(current.Window.Title) ? id : $"{current.Window.Title} (cópia)";
        var window = NormalizeWindow(current.Window with { Title = title });

        var duplicate = current with
        {
            Id = id,
            Window = window,
            BrowserArguments = current.BrowserArguments?.ToList() ?? new List<string>(),
            AllowedTabHosts = current.AllowedTabHosts?.ToList() ?? new List<string>(),
        };

        UpdateSelectedMonitor(duplicate.TargetMonitorStableId);
        _selectedEntry = EntryReference.Create(EntryKind.Site, duplicate.Id);
        _workspace.Sites.Add(duplicate);
        Log.Information("Sites:Duplicate -> {Name}", $"{duplicate.Id}|{duplicate.Url}");

        RefreshSitesList();
        SelectEntryInList(_selectedEntry);
        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Site '{duplicate.Id}' duplicado.");
    }

    private async void OnTestSiteClicked(object? sender, EventArgs e)
    {
        var site = GetSelectedSite();
        if (site is null)
        {
            return;
        }

        if (_siteTestButton is not null)
        {
            _siteTestButton.Enabled = false;
        }

        try
        {
            await RunSiteTestAsync(site, this, updateStatus: true).ConfigureAwait(true);
        }
        finally
        {
            UpdateSiteButtons();
        }
    }

    private bool TestSite(SiteConfig site, WinForms.Control owner)
    {
        try
        {
            return RequiresSelenium(site)
                ? TestSiteWithSelenium(site, owner)
                : TestSiteWithProcess(site, owner);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao testar site {SiteId}.", site.Id);
            owner.BeginInvoke(new Action(() =>
                WinForms.MessageBox.Show(owner, $"Falha ao testar o site: {ex.Message}", "Teste de site", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error)));
            return false;
        }
    }

    private async Task<bool> RunSiteTestAsync(SiteConfig site, WinForms.Control owner, bool updateStatus)
    {
        if (updateStatus)
        {
            UpdateStatus($"Testando site '{site.Id}'...");
        }

        Log.Information("Sites:Test -> {Name}", $"{site.Id}|{site.Url}");
        var success = await Task.Run(() => TestSite(site, owner)).ConfigureAwait(true);

        var monitor = ResolveTargetMonitor(site);
        var result = success ? "ok" : "fail";
        Log.Information(
            "Test(Site) -> monitor={Monitor}, zone={Zone}, result={Result}",
            ResolveMonitorStableId(monitor),
            DescribeZone(site),
            result);

        if (updateStatus)
        {
            UpdateStatus(success
                ? $"Site '{site.Id}' iniciado com sucesso."
                : $"Falha ao testar '{site.Id}'. Consulte os logs para mais detalhes.");
        }

        return success;
    }

    private static bool RequiresSelenium(SiteConfig site)
    {
        if (site.Login is { } login)
        {
            if (!string.IsNullOrWhiteSpace(login.Username)
                || !string.IsNullOrWhiteSpace(login.Password)
                || !string.IsNullOrWhiteSpace(login.UserSelector)
                || !string.IsNullOrWhiteSpace(login.PassSelector)
                || !string.IsNullOrWhiteSpace(login.SubmitSelector)
                || !string.IsNullOrWhiteSpace(login.Script))
            {
                return true;
            }
        }

        return site.AllowedTabHosts?.Any(host => !string.IsNullOrWhiteSpace(host)) == true;
    }

    private bool TestSiteWithProcess(SiteConfig site, WinForms.Control owner)
    {
        var executable = ResolveBrowserExecutable(site.Browser);
        var arguments = BuildBrowserArgumentString(site);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            owner.BeginInvoke(new Action(() =>
                WinForms.MessageBox.Show(owner, "Falha ao iniciar o navegador.", "Teste de site", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error)));
            return false;
        }

        var monitor = ResolveTargetMonitor(site);
        var zoneRect = ResolveZoneRect(monitor, site.TargetZonePresetId, site.Window);
        var topMost = site.Window.AlwaysOnTop || (zoneRect.WidthPercentage >= 99.5 && zoneRect.HeightPercentage >= 99.5);

        WindowPlacementHelper.ForcePlaceProcessWindowAsync(
            process,
            monitor,
            zoneRect,
            topMost,
            CancellationToken.None).GetAwaiter().GetResult();

        process.Refresh();
        if (process.MainWindowHandle == IntPtr.Zero)
        {
            owner.BeginInvoke(new Action(() =>
                WinForms.MessageBox.Show(owner, "O navegador foi iniciado, mas a janela não foi encontrada.", "Teste de site", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning)));
        }

        return true;
    }

    private bool TestSiteWithSelenium(SiteConfig site, WinForms.Control owner)
    {
        IWebDriver? driver = null;

        try
        {
            var arguments = CollectBrowserArguments(site).ToList();
            driver = WebDriverFactory.Create(site, arguments);
            ApplyWhitelist(driver, site.AllowedTabHosts ?? Array.Empty<string>());
            ExecuteLoginAsync(driver, site.Login).GetAwaiter().GetResult();

            var monitor = ResolveTargetMonitor(site);
            var zoneRect = ResolveZoneRect(monitor, site.TargetZonePresetId, site.Window);
            var bounds = WindowPlacementHelper.CalculateZoneBounds(monitor, zoneRect);

            if (OperatingSystem.IsWindows())
            {
                var handleString = driver.CurrentWindowHandle;
                if (long.TryParse(handleString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handleValue))
                {
                    var handle = new IntPtr(handleValue);
                    TryPositionWindow(site, handle);
                }
                else
                {
                    driver.Manage().Window.Position = new System.Drawing.Point(bounds.Left, bounds.Top);
                    driver.Manage().Window.Size = new System.Drawing.Size(bounds.Width, bounds.Height);
                }
            }
            else
            {
                driver.Manage().Window.Position = new System.Drawing.Point(bounds.Left, bounds.Top);
                driver.Manage().Window.Size = new System.Drawing.Size(bounds.Width, bounds.Height);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao testar site via Selenium {SiteId}.", site.Id);
            owner.BeginInvoke(new Action(() =>
                WinForms.MessageBox.Show(owner, $"Falha ao iniciar o Selenium: {ex.Message}", "Teste de site", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error)));
            return false;
        }
        finally
        {
            driver?.Quit();
        }
    }

    private string BuildBrowserArgumentString(SiteConfig site)
    {
        var arguments = CollectBrowserArguments(site).ToList();
        if (!site.AppMode && !string.IsNullOrWhiteSpace(site.Url))
        {
            arguments.Add(site.Url);
        }

        return string.Join(' ', arguments);
    }

    private IEnumerable<string> CollectBrowserArguments(SiteConfig site)
    {
        var arguments = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddArgument(string? argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return;
            }

            if (seen.Add(argument))
            {
                arguments.Add(argument);
            }
        }

        foreach (var argument in GetGlobalBrowserArguments(site.Browser))
        {
            AddArgument(argument);
        }

        foreach (var argument in site.BrowserArguments ?? Array.Empty<string>())
        {
            AddArgument(argument);
        }

        if (!string.IsNullOrWhiteSpace(site.UserDataDirectory) && !ContainsArgument(arguments, "--user-data-dir", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--user-data-dir", site.UserDataDirectory));
        }

        if (!string.IsNullOrWhiteSpace(site.ProfileDirectory) && !ContainsArgument(arguments, "--profile-directory", matchByPrefix: true))
        {
            AddArgument(FormatArgument("--profile-directory", site.ProfileDirectory));
        }

        if (site.KioskMode && !ContainsArgument(arguments, "--kiosk"))
        {
            AddArgument("--kiosk");
        }

        if (site.AppMode)
        {
            if (!string.IsNullOrWhiteSpace(site.Url) && !ContainsArgument(arguments, "--app", matchByPrefix: true))
            {
                AddArgument(FormatArgument("--app", site.Url));
            }
        }

        return arguments;
    }

    private IEnumerable<string> GetGlobalBrowserArguments(BrowserType browser)
    {
        return browser switch
        {
            BrowserType.Chrome => _workspace.BrowserArguments.Chrome ?? Array.Empty<string>(),
            BrowserType.Edge => _workspace.BrowserArguments.Edge ?? Array.Empty<string>(),
            _ => Array.Empty<string>(),
        };
    }

    private static bool ContainsArgument(IEnumerable<string> arguments, string name, bool matchByPrefix = false)
    {
        foreach (var argument in arguments)
        {
            if (matchByPrefix)
            {
                if (argument.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(argument, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatArgument(string name, string value)
    {
        var sanitized = value.Replace("\"", "\\\"");
        return $"{name}=\"{sanitized}\"";
    }

    private string ResolveBrowserExecutable(BrowserType browser)
    {
        if (OperatingSystem.IsWindows())
        {
            return browser switch
            {
                BrowserType.Chrome => "chrome.exe",
                BrowserType.Edge => "msedge.exe",
                _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
            };
        }

        return browser switch
        {
            BrowserType.Chrome => "google-chrome",
            BrowserType.Edge => "microsoft-edge",
            _ => throw new NotSupportedException($"Browser '{browser}' is not supported."),
        };
    }

    private void ApplyWhitelist(IWebDriver driver, IEnumerable<string> hosts)
    {
        var sanitized = hosts?.Where(static host => !string.IsNullOrWhiteSpace(host)).ToList();
        if (sanitized is null || sanitized.Count == 0)
        {
            return;
        }

        var tabManager = new TabManager(_telemetry);
        var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _ = Task.Run(() => tabManager.MonitorAsync(driver, sanitized, cancellation.Token));
    }

    private async Task ExecuteLoginAsync(IWebDriver driver, LoginProfile? login)
    {
        if (login is null)
        {
            return;
        }

        var loginService = new LoginService(_telemetry);
        try
        {
            await loginService.TryLoginAsync(driver, login, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falha durante a automação de login.");
        }
    }

    private void InitializeComponent()
    {
    }

}

