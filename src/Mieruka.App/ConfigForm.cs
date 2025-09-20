using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Controls;
using Mieruka.App.Ui;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;
using Mieruka.Core.Services;
using Serilog;

namespace Mieruka.App;

/// <summary>
/// Main window used to assign applications and sites to monitors while exposing
/// validation feedback about the configuration.
/// </summary>
internal sealed class ConfigForm : Form
{
    private readonly ConfiguratorWorkspace _workspace;
    private readonly JsonStore<GeneralConfig> _store;
    private readonly ConfigMigrator _migrator;
    private readonly IDisplayService? _displayService;
    private readonly ConfigValidator _validator;
    private readonly ImageList _imageList;
    private readonly ImageList _issueImageList;
    private readonly ListView _applicationsList;
    private readonly ListView _sitesList;
    private readonly ListView _issuesList;
    private const double ContentSplitterRatio = 0.35;
    private const double LayoutSplitterRatio = 0.35;
    private static readonly Size DefaultMinimumSizeLogical = new(960, 600);
    private const int ContentPanel1MinLogical = 120;
    private const int ContentPanel2MinLogical = 160;
    private const int LayoutPanel1MinLogical = 140;
    private const int LayoutPanel2MinLogical = 200;

    private readonly FlowLayoutPanel _monitorPanel;
    private readonly SplitContainer _layoutContainer;
    private readonly SplitContainer _contentContainer;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly List<MonitorPreviewControl> _monitorPreviews = new();
    private ConfigValidationReport _validationReport = ConfigValidationReport.Empty;

    private readonly ITelemetry _telemetry;
    private readonly MonitorSeeder _monitorSeeder;
    private readonly ToolTip _toolTip;
    private readonly FlowLayoutPanel _footerPanel;

    private EntryReference? _selectedEntry;
    private bool _isUpdatingSelection;

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

        _toolTip = new ToolTip
        {
            AutoPopDelay = 8000,
            InitialDelay = 400,
            ReshowDelay = 200,
            ShowAlways = true,
        };

        _footerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8),
        };

        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "MRK Configurator";
        StartPosition = FormStartPosition.CenterScreen;
        var minimumWidth = LogicalToDeviceUnits(DefaultMinimumSizeLogical.Width);
        var minimumHeight = LogicalToDeviceUnits(DefaultMinimumSizeLogical.Height);
        MinimumSize = new Size(minimumWidth, minimumHeight);

        var menuStrip = BuildMenu();

        _imageList = new ImageList
        {
            ImageSize = new Size(32, 32),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        _imageList.Images.Add("app", SystemIcons.Application);
        _imageList.Images.Add("site", SystemIcons.Information);

        _issueImageList = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit,
        };
        _issueImageList.Images.Add("error", SystemIcons.Error);
        _issueImageList.Images.Add("warning", SystemIcons.Warning);

        _applicationsList = CreateListView();
        _applicationsList.LargeImageList = _imageList;
        _applicationsList.SmallImageList = _imageList;
        _applicationsList.SelectedIndexChanged += OnApplicationsSelectedIndexChanged;
        _applicationsList.ItemDrag += OnListItemDrag;

        _sitesList = CreateListView();
        _sitesList.LargeImageList = _imageList;
        _sitesList.SmallImageList = _imageList;
        _sitesList.SelectedIndexChanged += OnSitesSelectedIndexChanged;
        _sitesList.ItemDrag += OnListItemDrag;

        var appsTab = new TabPage("Aplicativos")
        {
            Padding = new Padding(4),
        };
        appsTab.Controls.Add(_applicationsList);

        var sitesTab = new TabPage("Sites")
        {
            Padding = new Padding(4),
        };
        sitesTab.Controls.Add(_sitesList);

        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
        };
        tabControl.TabPages.Add(appsTab);
        tabControl.TabPages.Add(sitesTab);

        var monitorContainer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        _monitorPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(4),
        };

        monitorContainer.Controls.Add(_monitorPanel);

        _contentContainer = new SplitContainer
        {
            Name = "ContentSplitContainer",
            Dock = DockStyle.Fill,
        };

        _contentContainer.Panel1.Controls.Add(tabControl);
        _contentContainer.Panel2.Controls.Add(monitorContainer);

        _issuesList = new ListView
        {
            Dock = DockStyle.Fill,
            MultiSelect = false,
            HideSelection = false,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            SmallImageList = _issueImageList,
        };
        _issuesList.Columns.Add("Tipo", 100, HorizontalAlignment.Left);
        _issuesList.Columns.Add("Mensagem", 400, HorizontalAlignment.Left);

        var issuesLabel = new Label
        {
            Dock = DockStyle.Top,
            Text = "Problemas detectados",
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
            Padding = new Padding(0, 0, 0, 6),
        };

        var issuesPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
        };

        issuesPanel.Controls.Add(_issuesList);
        issuesPanel.Controls.Add(issuesLabel);

        _layoutContainer = new SplitContainer
        {
            Name = "LayoutSplitContainer",
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
        };

        _layoutContainer.Panel1.Controls.Add(issuesPanel);
        _layoutContainer.Panel2.Controls.Add(_contentContainer);
        _layoutContainer.Panel1Collapsed = true;

        _statusLabel = new ToolStripStatusLabel("Arraste um item para um monitor e selecione a área desejada.");
        _statusStrip = new StatusStrip();
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

        _footerPanel.Controls.AddRange(new Control[]
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
        ApplyInitialSplitters();

        Controls.Add(_layoutContainer);
        Controls.Add(_footerPanel);
        Controls.Add(_statusStrip);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;

        PopulateLists();
        BuildMonitorPreviews();
        RefreshValidation();

        if (_displayService is not null)
        {
            _displayService.TopologyChanged += OnTopologyChanged;
        }
    }

    private MenuStrip BuildMenu()
    {
        var menuStrip = new MenuStrip
        {
            Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont,
        };

        var fileMenu = new ToolStripMenuItem("Arquivo");

        var importItem = new ToolStripMenuItem("Importar...");
        importItem.Click += OnImportConfiguration;

        var exportItem = new ToolStripMenuItem("Exportar...");
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
            }

            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        try
        {
            var config = _workspace.BuildConfiguration();
            var migrated = _migrator.Migrate(config);
            _store.SaveAsync(migrated).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao salvar a configuração.");
            MessageBox.Show(this, $"Não foi possível salvar as alterações: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnImportConfiguration(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Configurações JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            Title = "Importar configuração",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
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
            MessageBox.Show(this, $"Não foi possível importar a configuração: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExportConfiguration(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Configurações JSON (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            Title = "Exportar configuração",
            FileName = "appsettings.json",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
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
            MessageBox.Show(this, $"Não foi possível exportar a configuração: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show(this, "Nenhum monitor foi detectado. Verifique as conexões e tente novamente.", "Detecção de Monitores", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            MessageBox.Show(this, $"Não foi possível detectar monitores: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSaveConfigurationClicked(object? sender, EventArgs e)
    {
        Log.Information("Solicitação de salvamento manual da configuração.");

        try
        {
            var config = _workspace.BuildConfiguration();
            var migrated = _migrator.Migrate(config);
            _store.SaveAsync(migrated).GetAwaiter().GetResult();
            UpdateStatus("Configuração salva com sucesso.");
            Log.Information("Configuração salva manualmente pelo usuário.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro ao salvar a configuração manualmente.");
            MessageBox.Show(this, $"Não foi possível salvar a configuração: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnValidateConfigurationClicked(object? sender, EventArgs e)
    {
        Log.Information("Validação manual da configuração iniciada.");
        RefreshValidation();

        var issueCount = _validationReport.Issues.Count;
        if (issueCount == 0)
        {
            UpdateStatus("Nenhum problema encontrado.");
            MessageBox.Show(this, "Nenhum problema foi encontrado na configuração.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Log.Information("Validação manual concluída sem problemas.");
        }
        else
        {
            UpdateStatus($"{issueCount} problema(s) encontrado(s).");
            MessageBox.Show(this, $"Foram encontrados {issueCount} problema(s). Consulte a lista para mais detalhes.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log.Information("Validação manual encontrou {IssueCount} problema(s).", issueCount);
        }
    }

    private void OnApplyPresetClicked(object? sender, EventArgs e)
    {
        Log.Information("Aplicação de preset solicitada.");

        if (_selectedEntry is null)
        {
            MessageBox.Show(this, "Selecione um aplicativo ou site antes de aplicar um preset.", "Aplicar Preset", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var config = _workspace.BuildConfiguration();
        if (config.ZonePresets.Count == 0)
        {
            MessageBox.Show(this, "Nenhum preset de zonas está disponível. Utilize 'Restaurar Padrão' para recriá-los.", "Aplicar Preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new PresetSelectionDialog(config.ZonePresets);
        if (dialog.ShowDialog(this) != DialogResult.OK)
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
            MessageBox.Show(this, "Nenhum monitor está configurado para exibição de preview.", "Preview", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show(this, $"Não foi possível abrir o preview: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, $"Não foi possível restaurar os presets padrão: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var item = new ListViewItem(severityText)
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
        _applicationsList.BeginUpdate();
        _applicationsList.Items.Clear();

        foreach (var app in _workspace.Applications)
        {
            var entry = EntryReference.Create(EntryKind.Application, app.Id);
            var displayName = string.IsNullOrWhiteSpace(app.Window.Title) ? app.Id : app.Window.Title;
            var item = new ListViewItem(displayName)
            {
                Tag = entry,
                ImageKey = "app",
                ToolTipText = app.ExecutablePath,
            };

            _applicationsList.Items.Add(item);
        }

        _applicationsList.EndUpdate();

        _sitesList.BeginUpdate();
        _sitesList.Items.Clear();

        foreach (var site in _workspace.Sites)
        {
            var entry = EntryReference.Create(EntryKind.Site, site.Id);
            var displayName = string.IsNullOrWhiteSpace(site.Window.Title) ? site.Id : site.Window.Title;
            var item = new ListViewItem(displayName)
            {
                Tag = entry,
                ImageKey = "site",
                ToolTipText = site.Url,
            };

            _sitesList.Items.Add(item);
        }

        _sitesList.EndUpdate();
    }

    private void BuildMonitorPreviews()
    {
        _monitorPanel.SuspendLayout();
        foreach (var preview in _monitorPreviews)
        {
            preview.EntryDropped -= OnMonitorEntryDropped;
            preview.SelectionApplied -= OnMonitorSelectionApplied;
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
                Margin = new Padding(8),
            };

            preview.Monitor = monitor;
            preview.EntryDropped += OnMonitorEntryDropped;
            preview.SelectionApplied += OnMonitorSelectionApplied;

            _monitorPreviews.Add(preview);
            _monitorPanel.Controls.Add(preview);
        }

        _monitorPanel.ResumeLayout();
        UpdateMonitorPreviews();
    }

    private static ListView CreateListView()
    {
        return new ListView
        {
            Dock = DockStyle.Fill,
            MultiSelect = false,
            HideSelection = false,
            ShowItemToolTips = true,
            View = View.Tile,
        };
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
            }
            else if (_sitesList.SelectedItems.Count == 0)
            {
                _selectedEntry = null;
            }

            UpdateMonitorPreviews();
            UpdateStatus();
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
            }
            else if (_applicationsList.SelectedItems.Count == 0)
            {
                _selectedEntry = null;
            }

            UpdateMonitorPreviews();
            UpdateStatus();
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void OnListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem item || item.Tag is not EntryReference entry)
        {
            return;
        }

        DoDragDrop(EntryReference.CreateDataObject(entry), DragDropEffects.Move);
    }

    private void OnMonitorEntryDropped(object? sender, MonitorPreviewControl.EntryDroppedEventArgs e)
    {
        if (!_workspace.TryAssignEntryToMonitor(e.Entry, e.Monitor))
        {
            MessageBox.Show(this, "Não foi possível aplicar a configuração ao monitor selecionado.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _selectedEntry = e.Entry;
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
            MessageBox.Show(this, "Não foi possível aplicar a seleção ao item atual.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        UpdateMonitorPreviews();
        RefreshValidation();
        UpdateStatus($"Área atualizada para {_selectedEntry.Id} em {e.Monitor.Name}.");
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

        foreach (var preview in _monitorPreviews)
        {
            if (window is not null && windowMonitor is not null && KeysEqual(windowMonitor.Key, preview.Monitor?.Key))
            {
                preview.DisplayWindow(window);
            }
            else
            {
                preview.DisplayWindow(null);
            }
        }
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

    private Button CreateFooterButton(string text, string toolTipText, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(toolTipText);
        ArgumentNullException.ThrowIfNull(handler);

        var font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4),
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
        MinimumSize = new Size(minWidth, minHeight);

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

    private void ApplySplitterRatio(SplitContainer? container, double ratio)
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

    private static bool TryGetAvailableLength(SplitContainer container, out int length)
    {
        length = container.Orientation == Orientation.Horizontal
            ? container.ClientSize.Height
            : container.ClientSize.Width;

        return length > 0;
    }

    private string GetContainerContext(SplitContainer container)
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
            MessageBox.Show(this, "O item selecionado ainda não possui uma janela configurada.", "Aplicar Preset", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var monitor = _workspace.FindMonitor(window.Monitor);
        if (monitor is null)
        {
            MessageBox.Show(this, "Associe o item a um monitor antes de aplicar um preset.", "Aplicar Preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var rectangle = CalculateZoneRectangle(zone, monitor);
        if (!_workspace.TryAssignEntryToMonitor(_selectedEntry, monitor, rectangle))
        {
            MessageBox.Show(this, "Não foi possível aplicar o preset ao item selecionado.", "Aplicar Preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private static Rectangle CalculateZoneRectangle(ZonePreset.Zone zone, MonitorInfo monitor)
    {
        var width = Math.Max(1, (int)Math.Round(monitor.Width * (zone.WidthPercentage / 100d)));
        var height = Math.Max(1, (int)Math.Round(monitor.Height * (zone.HeightPercentage / 100d)));
        var x = (int)Math.Round(monitor.Width * (zone.LeftPercentage / 100d));
        var y = (int)Math.Round(monitor.Height * (zone.TopPercentage / 100d));

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

        x = Math.Clamp(x, 0, Math.Max(0, monitor.Width - width));
        y = Math.Clamp(y, 0, Math.Max(0, monitor.Height - height));

        return new Rectangle(x, y, width, height);
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
}
