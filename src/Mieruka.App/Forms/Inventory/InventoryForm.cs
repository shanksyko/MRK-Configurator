#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.EntityFrameworkCore;
using Mieruka.Core.Data;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;
using Mieruka.Core.Models;
using Mieruka.App.Services.Ui;
using Serilog;

namespace Mieruka.App.Forms.Inventory;

public sealed class InventoryForm : Form
{
    private static readonly ILogger Logger = Log.ForContext<InventoryForm>();

    private readonly MierukaDbContext _db;
    private readonly InventoryService _inventoryService;
    private readonly InventoryCategoryService _categoryService;
    private readonly InventoryMovementService _movementService;
    private readonly MaintenanceRecordService _maintenanceService;
    private readonly IReadOnlyList<MonitorInfo> _monitors;

    private List<InventoryItemEntity> _allItems = new();
    private List<InventoryCategoryEntity> _categories = new();
    private string? _selectedCategory;

    // Controls
    private readonly TreeView _treeCategories = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbStatus = new();
    private readonly ToolStrip _toolStrip = new();
    private readonly Label _lblCount = new();

    private ToolStripButton _btnNew = null!;
    private ToolStripButton _btnEdit = null!;
    private ToolStripButton _btnDelete = null!;
    private ToolStripButton _btnMove = null!;
    private ToolStripButton _btnMaintenance = null!;
    private ToolStripButton _btnHistory = null!;
    private ToolStripButton _btnRefresh = null!;
    private ToolStripButton _btnCategories = null!;
    private ToolStripButton _btnDashboard = null!;
    private ToolStripDropDownButton _btnExport = null!;
    private ToolStripDropDownButton _btnImport = null!;

    public InventoryForm(
        MierukaDbContext db,
        InventoryService inventoryService,
        InventoryCategoryService categoryService,
        InventoryMovementService movementService,
        MaintenanceRecordService maintenanceService,
        IReadOnlyList<MonitorInfo>? monitors = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));
        _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _monitors = monitors ?? Array.Empty<MonitorInfo>();

        BuildLayout();
        Shown += async (_, _) => await LoadAllAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        Text = "Inventário";
        ClientSize = new Size(1100, 680);
        MinimumSize = new Size(800, 500);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9f);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        DoubleBuffered = true;

        // ── ToolStrip ──────────────────────────────────────────────────────────
        BuildToolStrip();

        // ── Main SplitContainer ───────────────────────────────────────────────
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 210,
            FixedPanel = FixedPanel.Panel1,
        };

        // Left panel — categories tree
        var lblCategories = new Label
        {
            Text = "Categorias",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Color.FromArgb(60, 60, 60),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            BackColor = Color.FromArgb(240, 240, 240),
        };
        _treeCategories.Dock = DockStyle.Fill;
        _treeCategories.HideSelection = false;
        _treeCategories.Font = new Font("Segoe UI", 9f);
        _treeCategories.ItemHeight = 24;
        _treeCategories.BorderStyle = BorderStyle.None;
        _treeCategories.BackColor = Color.White;
        _treeCategories.AfterSelect += (_, _) => ApplyFilter();
        split.Panel1.Controls.Add(_treeCategories);
        split.Panel1.Controls.Add(lblCategories);

        // Right panel — grid + search bar + status bar (search below the grid)
        var rightPanel = new Panel { Dock = DockStyle.Fill };

        // Status bar (Dock.Bottom — added first so it docks last)
        _lblCount.AutoSize = false;
        _lblCount.Text = "0 itens";
        _lblCount.Dock = DockStyle.Bottom;
        _lblCount.Height = 24;
        _lblCount.Padding = new Padding(6, 0, 0, 0);
        _lblCount.Font = new Font("Segoe UI", 8.5f);
        _lblCount.ForeColor = Color.FromArgb(100, 100, 100);
        _lblCount.TextAlign = ContentAlignment.MiddleLeft;
        _lblCount.BackColor = Color.FromArgb(245, 245, 245);

        // Search bar (Dock.Bottom — sits above status bar)
        var searchPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            AutoSize = false,
            Padding = new Padding(6, 6, 6, 6),
            WrapContents = false,
            BackColor = Color.FromArgb(248, 248, 248),
        };

        var lblSearch = new Label { Text = "Buscar:", AutoSize = true, Margin = new Padding(0, 4, 4, 0), Font = new Font("Segoe UI", 9f) };
        _txtSearch.Width = 240;
        _txtSearch.Margin = new Padding(0, 0, 12, 0);
        _txtSearch.Font = new Font("Segoe UI", 9f);
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        var lblStatus = new Label { Text = "Status:", AutoSize = true, Margin = new Padding(0, 4, 4, 0), Font = new Font("Segoe UI", 9f) };
        _cmbStatus.Width = 140;
        _cmbStatus.Font = new Font("Segoe UI", 9f);
        _cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStatus.Items.Add("(Todos)");
        foreach (var status in Mieruka.Core.Data.Services.InventoryItemStatus.All)
            _cmbStatus.Items.Add(status);
        _cmbStatus.SelectedIndex = 0;
        _cmbStatus.SelectedIndexChanged += (_, _) => ApplyFilter();

        searchPanel.Controls.Add(lblSearch);
        searchPanel.Controls.Add(_txtSearch);
        searchPanel.Controls.Add(lblStatus);
        searchPanel.Controls.Add(_cmbStatus);

        // Grid (Dock.Fill — fills remaining space above search bar)
        BuildGrid();
        _grid.Dock = DockStyle.Fill;

        // WinForms docking z-order: last-added control docks first.
        // Add Fill first, then Bottom controls, so Bottom reserves space before Fill expands.
        rightPanel.Controls.Add(_grid);
        rightPanel.Controls.Add(searchPanel);
        rightPanel.Controls.Add(_lblCount);

        split.Panel2.Controls.Add(rightPanel);

        // WinForms z-order: last-added control is docked first.
        // Add Fill first, then Top last, so ToolStrip reserves space before split expands.
        Controls.Add(split);
        Controls.Add(_toolStrip);
    }

    private void BuildToolStrip()
    {
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.RenderMode = ToolStripRenderMode.System;
        _toolStrip.Dock = DockStyle.Top;
        _toolStrip.Font = new Font("Segoe UI", 9f);
        _toolStrip.Padding = new Padding(4, 0, 4, 0);
        _toolStrip.BackColor = Color.FromArgb(248, 248, 248);

        _btnNew = new ToolStripButton("Novo", null, async (_, _) => await OnNewClickedAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnEdit = new ToolStripButton("Editar", null, async (_, _) => await OnEditClickedAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnDelete = new ToolStripButton("Excluir", null, async (_, _) => await OnDeleteClickedAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnMove = new ToolStripButton("Movimentação", null, async (_, _) => await OnMoveClickedAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnMaintenance = new ToolStripButton("Manutenção", null, async (_, _) => await OnMaintenanceClickedAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnHistory = new ToolStripButton("Histórico", null, async (_, _) => await OnHistoryClickedAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnRefresh = new ToolStripButton("Atualizar", null, async (_, _) => await LoadAllAsync()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnCategories = new ToolStripButton("Categorias", null, (_, _) => OnCategoriesClicked()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnDashboard = new ToolStripButton("Dashboard", null, (_, _) => OnDashboardClicked()) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnExport = new ToolStripDropDownButton("Exportar") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnExport.DropDownItems.Add("Exportar CSV", null, (_, _) => OnExportCsvClicked());
        _btnExport.DropDownItems.Add("Exportar Access (.accdb)", null, async (_, _) => await OnExportAccessClickedAsync());
        _btnExport.DropDownItems.Add("Exportar SQL Server (.mdf)", null, async (_, _) => await OnExportSqlServerClickedAsync());
        _btnExport.DropDownItems.Add("Exportar SQL Server (Servidor)", null, async (_, _) => await OnExportRemoteSqlServerClickedAsync());
        _btnImport = new ToolStripDropDownButton("Importar") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        _btnImport.DropDownItems.Add("Importar Access (.accdb)", null, async (_, _) => await OnImportAccessClickedAsync());
        _btnImport.DropDownItems.Add("Importar SQL Server (.mdf)", null, async (_, _) => await OnImportSqlServerClickedAsync());
        _btnImport.DropDownItems.Add("Importar SQL Server (Servidor)", null, async (_, _) => await OnImportRemoteSqlServerClickedAsync());

        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _btnNew,
            _btnEdit,
            _btnDelete,
            new ToolStripSeparator(),
            _btnMove,
            _btnMaintenance,
            _btnHistory,
            new ToolStripSeparator(),
            _btnCategories,
            _btnDashboard,
            _btnExport,
            _btnImport,
            new ToolStripSeparator(),
            _btnRefresh,
        });
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Color.FromArgb(230, 230, 230);
        _grid.SelectionChanged += (_, _) => UpdateButtonStates();
        _grid.CellDoubleClick += async (_, e) => { if (e.RowIndex >= 0) await OnEditClickedAsync(); };

        // Header styling
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(240, 240, 240),
            ForeColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI Semibold", 9f),
            Padding = new Padding(4, 4, 4, 4),
            SelectionBackColor = Color.FromArgb(240, 240, 240),
            SelectionForeColor = Color.FromArgb(60, 60, 60),
        };
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 32;

        // Row styling
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(4, 2, 4, 2),
            SelectionBackColor = Color.FromArgb(0, 120, 215),
            SelectionForeColor = Color.White,
        };
        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(245, 248, 255),
        };
        _grid.RowTemplate.Height = 28;

        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "colName",         HeaderText = "Nome",         FillWeight = 22, MinimumWidth = 120 },
            new DataGridViewTextBoxColumn { Name = "colCategory",     HeaderText = "Categoria",    FillWeight = 14, MinimumWidth = 80  },
            new DataGridViewTextBoxColumn { Name = "colStatus",       HeaderText = "Status",       FillWeight = 12, MinimumWidth = 70  },
            new DataGridViewTextBoxColumn { Name = "colSerialNumber", HeaderText = "Nº de Série",  FillWeight = 14, MinimumWidth = 80  },
            new DataGridViewTextBoxColumn { Name = "colLocation",     HeaderText = "Localização",  FillWeight = 14, MinimumWidth = 80  },
            new DataGridViewTextBoxColumn { Name = "colAssignedTo",   HeaderText = "Responsável",  FillWeight = 14, MinimumWidth = 80  },
            new DataGridViewTextBoxColumn { Name = "colWarranty",     HeaderText = "Garantia",     FillWeight = 10, MinimumWidth = 70  },
        });
        DoubleBufferingHelper.EnableOptimizedDoubleBuffering(_grid);
    }

    // ── Data Loading ──────────────────────────────────────────────────────────

    private async Task LoadAllAsync()
    {
        try
        {
            _categories = (await _categoryService.GetAllAsync().ConfigureAwait(true)).ToList();
            _allItems = (await _inventoryService.GetAllAsync().ConfigureAwait(true)).ToList();
            RebuildCategoryTree();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar inventário: {ex.Message}", "Inventário",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RebuildCategoryTree()
    {
        _treeCategories.BeginUpdate();
        _treeCategories.Nodes.Clear();

        var root = _treeCategories.Nodes.Add("Todas as categorias");
        root.Tag = null;

        var categoryNames = _allItems.Select(i => i.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        foreach (var cat in categoryNames)
        {
            var node = root.Nodes.Add(cat);
            node.Tag = cat;
        }

        _treeCategories.ExpandAll();
        _treeCategories.SelectedNode = root;
        _treeCategories.EndUpdate();
    }

    private void ApplyFilter()
    {
        _selectedCategory = _treeCategories.SelectedNode?.Tag as string;
        var search = _txtSearch.Text.Trim();
        var statusFilter = _cmbStatus.SelectedItem as string;

        var filtered = _allItems.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_selectedCategory))
        {
            filtered = filtered.Where(i =>
                string.Equals(i.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(i =>
                Contains(i.Name, search) ||
                Contains(i.SerialNumber, search) ||
                Contains(i.AssetTag, search) ||
                Contains(i.Location, search) ||
                Contains(i.AssignedTo, search));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "(Todos)")
        {
            filtered = filtered.Where(i =>
                string.Equals(i.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        PopulateGrid(list);
        _lblCount.Text = $"{list.Count} item(ns)";
        UpdateButtonStates();
    }

    private static bool Contains(string? text, string search) =>
        text is not null && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

    private void PopulateGrid(List<InventoryItemEntity> items)
    {
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();

            foreach (var item in items)
            {
                var rowIndex = _grid.Rows.Add();
                var row = _grid.Rows[rowIndex];
                row.Cells["colName"].Value = item.Name;
                row.Cells["colCategory"].Value = item.Category;
                row.Cells["colStatus"].Value = item.Status;
                row.Cells["colSerialNumber"].Value = item.SerialNumber;
                row.Cells["colLocation"].Value = item.Location;
                row.Cells["colAssignedTo"].Value = item.AssignedTo;
                row.Cells["colWarranty"].Value = item.WarrantyExpiresAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? string.Empty;
                row.Tag = item;
            }
        }
        finally
        {
            _grid.ResumeLayout(true);
        }
    }

    private InventoryItemEntity? GetSelectedItem()
    {
        if (_grid.SelectedRows.Count == 0)
        {
            return null;
        }

        return _grid.SelectedRows[0].Tag as InventoryItemEntity;
    }

    private void UpdateButtonStates()
    {
        var hasSelection = GetSelectedItem() is not null;
        _btnEdit.Enabled = hasSelection;
        _btnDelete.Enabled = hasSelection;
        _btnMove.Enabled = hasSelection;
        _btnMaintenance.Enabled = hasSelection;
        _btnHistory.Enabled = hasSelection;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task OnNewClickedAsync()
    {
        using var editor = new InventoryItemEditorForm(null, _categories, _monitors);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
        {
            return;
        }

        try
        {
            await _inventoryService.CreateAsync(editor.Result).ConfigureAwait(true);
            await LoadAllAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao salvar item: {ex.Message}", "Inventário",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnEditClickedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        using var editor = new InventoryItemEditorForm(item, _categories, _monitors);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
        {
            return;
        }

        try
        {
            await _inventoryService.UpdateAsync(editor.Result).ConfigureAwait(true);
            await LoadAllAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao atualizar item: {ex.Message}", "Inventário",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnDeleteClickedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Deseja excluir o item '{item.Name}'?",
            "Excluir Item",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _inventoryService.DeleteAsync(item.Id).ConfigureAwait(true);
            await LoadAllAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao excluir item: {ex.Message}", "Inventário",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnMoveClickedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        using var dlg = new MovementDialogForm(item);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var isSaida = string.Equals(dlg.MovementType, "Saída", StringComparison.Ordinal);

            if (isSaida)
            {
                if (string.IsNullOrWhiteSpace(dlg.ExitReason))
                {
                    MessageBox.Show("O motivo da saída é obrigatório.", "Validação",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (dlg.ExitQuantity > item.Quantity)
                {
                    MessageBox.Show($"Quantidade de saída ({dlg.ExitQuantity}) excede o disponível ({item.Quantity}).",
                        "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            using var transaction = await _db.Database.BeginTransactionAsync().ConfigureAwait(true);
            try
            {
                var notes = dlg.Notes;
                if (isSaida)
                {
                    notes = $"[Saída: Qtd={dlg.ExitQuantity}] Motivo: {dlg.ExitReason}" +
                        (string.IsNullOrWhiteSpace(notes) ? "" : $" | {notes}");
                }

                await _movementService.RecordMovementAsync(
                    item.Id,
                    dlg.MovementType,
                    item.Location,
                    dlg.ToLocation,
                    item.AssignedTo,
                    dlg.ToAssignee,
                    dlg.PerformedBy,
                    notes).ConfigureAwait(true);

                // Update item location/assignee
                item.Location = dlg.ToLocation ?? item.Location;
                item.AssignedTo = dlg.ToAssignee ?? item.AssignedTo;

                if (isSaida)
                {
                    item.Quantity -= dlg.ExitQuantity;

                    if (item.Quantity <= 0)
                    {
                        var answer = MessageBox.Show(
                            this,
                            "A quantidade chegou a zero. Deseja marcar o item como 'Disposed'?",
                            "Saída de Material",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);
                        if (answer == DialogResult.Yes)
                        {
                            item.Status = InventoryItemStatus.Disposed;
                        }
                    }
                }

                await _inventoryService.UpdateAsync(item).ConfigureAwait(true);
                await transaction.CommitAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Falha na transação de movimentação; executando rollback.");
                await transaction.RollbackAsync().ConfigureAwait(true);
                throw;
            }

            await LoadAllAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao registrar movimentação: {ex.Message}", "Inventário",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnMaintenanceClickedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        using var dlg = new MaintenanceDialogForm(item, _maintenanceService);
        _ = dlg.ShowDialog(this);
        await LoadAllAsync().ConfigureAwait(true);
    }

    private async Task OnHistoryClickedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        using var form = new MovementHistoryForm(item.Id, item.Name, _movementService);
        form.ShowDialog(this);
        await Task.CompletedTask.ConfigureAwait(true);
    }

    // ── Categorias / Dashboard / Export ───────────────────────────────────────

    private async void OnCategoriesClicked()
    {
        try
        {
            using var form = new CategoryEditorForm(_categoryService);
            form.ShowDialog(this);
            await LoadAllAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Erro ao recarregar dados após edição de categorias: {ex.Message}",
                "Categorias", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDashboardClicked()
    {
        using var form = new InventoryDashboardForm(_inventoryService, _maintenanceService, _categoryService);
        form.ShowDialog(this);
    }

    private void OnExportCsvClicked()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar Inventário",
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = $"inventario_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            const char sep = ';';
            var sb = new StringBuilder();
            sb.AppendLine("Nome;Categoria;Status;Nº Série;Patrimônio;Fabricante;Modelo;Localização;Responsável;Quantidade;Custo Unitário;Garantia;Notas");

            foreach (var item in _allItems)
            {
                sb.Append(CsvEscape(item.Name)).Append(sep);
                sb.Append(CsvEscape(item.Category)).Append(sep);
                sb.Append(CsvEscape(item.Status)).Append(sep);
                sb.Append(CsvEscape(item.SerialNumber)).Append(sep);
                sb.Append(CsvEscape(item.AssetTag)).Append(sep);
                sb.Append(CsvEscape(item.Manufacturer)).Append(sep);
                sb.Append(CsvEscape(item.Model)).Append(sep);
                sb.Append(CsvEscape(item.Location)).Append(sep);
                sb.Append(CsvEscape(item.AssignedTo)).Append(sep);
                sb.Append(item.Quantity).Append(sep);
                sb.Append(item.UnitCostCents.HasValue ? (item.UnitCostCents.Value / 100.0).ToString("F2") : "").Append(sep);
                sb.Append(item.WarrantyExpiresAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "").Append(sep);
                sb.AppendLine(CsvEscape(item.Notes));
            }

            // UTF-8 with BOM so Excel recognises the encoding and accented characters.
            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show($"Exportados {_allItems.Count} item(ns) para:\n{dlg.FileName}",
                "Exportação", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao exportar: {ex.Message}", "Exportação",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private async Task OnExportAccessClickedAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar Inventário para Access",
            Filter = "Access Database (*.accdb)|*.accdb",
            DefaultExt = "accdb",
            FileName = $"inventario_{DateTime.Now:yyyyMMdd_HHmmss}.accdb",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var exportService = new InventoryExportService(_db);
            await exportService.ExportToAccessAsync(dlg.FileName);
            MessageBox.Show($"Exportados {_allItems.Count} item(ns) para:\n{dlg.FileName}",
                "Exportação Access", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao exportar inventário para Access.");
            MessageBox.Show(
                $"Erro ao exportar para Access: {ex.Message}\n\n" +
                "Verifique se o Microsoft Access Database Engine está instalado:\n" +
                "https://www.microsoft.com/pt-br/download/details.aspx?id=54920",
                "Exportação Access", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task OnExportSqlServerClickedAsync()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Exportar Inventário para SQL Server",
            Filter = "SQL Server Database (*.mdf)|*.mdf",
            DefaultExt = "mdf",
            FileName = $"inventario_{DateTime.Now:yyyyMMdd_HHmmss}.mdf",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var exportService = new InventoryExportService(_db);
            await exportService.ExportToSqlServerAsync(dlg.FileName);
            MessageBox.Show($"Exportados {_allItems.Count} item(ns) para:\n{dlg.FileName}",
                "Exportação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao exportar inventário para SQL Server MDF.");
            MessageBox.Show(
                $"Erro ao exportar para SQL Server: {ex.Message}\n\n" +
                "Verifique se o SQL Server LocalDB está instalado:\n" +
                "https://learn.microsoft.com/pt-br/sql/database-engine/configure-windows/sql-server-express-localdb",
                "Exportação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // ── Importação ────────────────────────────────────────────────────────────

    private async Task OnImportAccessClickedAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Importar Inventário de Access",
            Filter = "Access Database (*.accdb)|*.accdb",
            DefaultExt = "accdb",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var replaceAll = AskImportMode();
        if (replaceAll is null)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var importService = new InventoryImportService(_db);
            var result = await importService.ImportFromAccessAsync(dlg.FileName, replaceAll.Value);
            await LoadAllAsync().ConfigureAwait(true);
            MessageBox.Show(
                $"Importação concluída:\n" +
                $"  {result.Categories} categorias\n" +
                $"  {result.Items} itens\n" +
                $"  {result.Movements} movimentações\n" +
                $"  {result.Maintenance} manutenções",
                "Importação Access", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao importar inventário de Access.");
            MessageBox.Show(
                $"Erro ao importar de Access: {ex.Message}\n\n" +
                "Verifique se o Microsoft Access Database Engine está instalado:\n" +
                "https://www.microsoft.com/pt-br/download/details.aspx?id=54920",
                "Importação Access", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task OnImportSqlServerClickedAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Importar Inventário de SQL Server",
            Filter = "SQL Server Database (*.mdf)|*.mdf",
            DefaultExt = "mdf",
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var replaceAll = AskImportMode();
        if (replaceAll is null)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var importService = new InventoryImportService(_db);
            var result = await importService.ImportFromSqlServerAsync(dlg.FileName, replaceAll.Value);
            await LoadAllAsync().ConfigureAwait(true);
            MessageBox.Show(
                $"Importação concluída:\n" +
                $"  {result.Categories} categorias\n" +
                $"  {result.Items} itens\n" +
                $"  {result.Movements} movimentações\n" +
                $"  {result.Maintenance} manutenções",
                "Importação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao importar inventário de SQL Server MDF.");
            MessageBox.Show(
                $"Erro ao importar de SQL Server: {ex.Message}\n\n" +
                "Verifique se o SQL Server LocalDB está instalado:\n" +
                "https://learn.microsoft.com/pt-br/sql/database-engine/configure-windows/sql-server-express-localdb",
                "Importação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>
    /// Pergunta ao usuário se deseja adicionar ou substituir dados.
    /// Retorna true para substituir, false para adicionar, null se cancelou.
    /// </summary>
    private bool? AskImportMode()
    {
        var result = MessageBox.Show(
            this,
            "Como deseja importar os dados?\n\n" +
            "SIM = Substituir todo o inventário atual pelos dados do arquivo.\n" +
            "NÃO = Adicionar os dados do arquivo ao inventário existente.\n" +
            "CANCELAR = Não importar.",
            "Modo de Importação",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => true,
            DialogResult.No => false,
            _ => null,
        };
    }

    /// <summary>
    /// Pergunta ao usuário se deseja substituir ou adicionar dados na exportação para servidor.
    /// Retorna true para substituir, false para adicionar, null se cancelou.
    /// </summary>
    private bool? AskExportMode()
    {
        var result = MessageBox.Show(
            this,
            "Como deseja exportar os dados para o servidor?\n\n" +
            "SIM = Substituir todos os dados no servidor pelos dados locais.\n" +
            "NÃO = Adicionar os dados locais ao servidor (manter existentes).\n" +
            "CANCELAR = Não exportar.",
            "Modo de Exportação",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        return result switch
        {
            DialogResult.Yes => true,
            DialogResult.No => false,
            _ => null,
        };
    }

    // ── Importação/Exportação SQL Server Remoto ──────────────────────────────

    private async Task OnImportRemoteSqlServerClickedAsync()
    {
        using var dlg = new SqlServerConnectionDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ConnectionString is null)
            return;

        var replaceAll = AskImportMode();
        if (replaceAll is null)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var importService = new InventoryImportService(_db);
            var result = await importService.ImportFromRemoteSqlServerAsync(dlg.ConnectionString, replaceAll.Value);
            await LoadAllAsync().ConfigureAwait(true);
            MessageBox.Show(
                $"Importação concluída:\n" +
                $"  {result.Categories} categorias\n" +
                $"  {result.Items} itens\n" +
                $"  {result.Movements} movimentações\n" +
                $"  {result.Maintenance} manutenções",
                "Importação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao importar inventário de SQL Server remoto.");
            MessageBox.Show(
                $"Erro ao importar de SQL Server: {ex.Message}",
                "Importação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task OnExportRemoteSqlServerClickedAsync()
    {
        using var dlg = new SqlServerConnectionDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ConnectionString is null)
            return;

        var replaceAll = AskExportMode();
        if (replaceAll is null)
            return;

        try
        {
            Cursor = Cursors.WaitCursor;
            var exportService = new InventoryExportService(_db);
            await exportService.ExportToRemoteSqlServerAsync(dlg.ConnectionString, replaceAll.Value);
            MessageBox.Show(
                $"Exportação para SQL Server concluída com sucesso.",
                "Exportação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Falha ao exportar inventário para SQL Server remoto.");
            MessageBox.Show(
                $"Erro ao exportar para SQL Server: {ex.Message}",
                "Exportação SQL Server", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}

// ── Simple inline dialogs ─────────────────────────────────────────────────────

internal sealed class MovementDialogForm : Form
{
    private readonly ComboBox _cmbType = new();
    private readonly TextBox _txtToLocation = new();
    private readonly TextBox _txtToAssignee = new();
    private readonly TextBox _txtPerformedBy = new();
    private readonly TextBox _txtNotes = new();
    private readonly NumericUpDown _numExitQuantity = new();
    private readonly TextBox _txtExitReason = new();
    private readonly Label _lblExitQuantity = new();
    private readonly Label _lblExitReason = new();
    private readonly Button _btnOk = new();
    private readonly Button _btnCancel = new();

    public string MovementType => (_cmbType.SelectedItem as string) ?? "Transfer";
    public string? ToLocation => string.IsNullOrWhiteSpace(_txtToLocation.Text) ? null : _txtToLocation.Text.Trim();
    public string? ToAssignee => string.IsNullOrWhiteSpace(_txtToAssignee.Text) ? null : _txtToAssignee.Text.Trim();
    public string? PerformedBy => string.IsNullOrWhiteSpace(_txtPerformedBy.Text) ? null : _txtPerformedBy.Text.Trim();
    public string? Notes => string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
    public int ExitQuantity => (int)_numExitQuantity.Value;
    public string? ExitReason => string.IsNullOrWhiteSpace(_txtExitReason.Text) ? null : _txtExitReason.Text.Trim();

    public MovementDialogForm(InventoryItemEntity item)
    {
        Text = $"Registrar Movimentação — {item.Name}";
        MinimumSize = new Size(440, 340);
        ClientSize = new Size(460, 380);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        _cmbType.Dock = DockStyle.Fill;
        _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var t in new[] { "Transfer", "Assignment", "Return", "Disposal", "Repair", "Saída" })
        {
            _cmbType.Items.Add(t);
        }
        _cmbType.SelectedIndex = 0;
        _cmbType.SelectedIndexChanged += (_, _) => UpdateExitFieldsVisibility();
        AddRow(layout, "Tipo:", _cmbType);

        _txtToLocation.Dock = DockStyle.Fill;
        _txtToLocation.Text = item.Location ?? string.Empty;
        AddRow(layout, "Para (Local):", _txtToLocation);

        _txtToAssignee.Dock = DockStyle.Fill;
        _txtToAssignee.Text = item.AssignedTo ?? string.Empty;
        AddRow(layout, "Para (Responsável):", _txtToAssignee);

        _txtPerformedBy.Dock = DockStyle.Fill;
        AddRow(layout, "Realizado por:", _txtPerformedBy);

        _txtNotes.Dock = DockStyle.Fill;
        AddRow(layout, "Notas:", _txtNotes);

        // Exit-specific fields
        _numExitQuantity.Dock = DockStyle.Left;
        _numExitQuantity.Minimum = 1;
        _numExitQuantity.Maximum = Math.Max(1, item.Quantity);
        _numExitQuantity.Value = 1;
        _numExitQuantity.Width = 100;
        _lblExitQuantity.Text = $"Quantidade (disponível: {item.Quantity}):";
        AddRowWithLabel(layout, _lblExitQuantity, _numExitQuantity);

        _txtExitReason.Dock = DockStyle.Fill;
        _lblExitReason.Text = "Motivo da saída:*";
        AddRowWithLabel(layout, _lblExitReason, _txtExitReason);

        // Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        _btnCancel.Text = "Cancelar";
        _btnCancel.AutoSize = false;
        _btnCancel.Size = new Size(90, 30);
        _btnCancel.FlatStyle = FlatStyle.Flat;
        _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnOk.BackColor = Color.FromArgb(0, 120, 215);
        _btnOk.FlatStyle = FlatStyle.Flat;
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.ForeColor = Color.White;
        _btnOk.Text = "Confirmar";
        _btnOk.AutoSize = false;
        _btnOk.Size = new Size(100, 30);
        _btnOk.UseVisualStyleBackColor = false;
        _btnOk.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        buttonPanel.Controls.Add(_btnCancel);
        buttonPanel.Controls.Add(_btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        UpdateExitFieldsVisibility();
    }

    private void UpdateExitFieldsVisibility()
    {
        var isSaida = string.Equals(_cmbType.SelectedItem as string, "Saída", StringComparison.Ordinal);
        _numExitQuantity.Visible = isSaida;
        _txtExitReason.Visible = isSaida;
        _lblExitQuantity.Visible = isSaida;
        _lblExitReason.Visible = isSaida;
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 6),
        }, 0, row);
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private static void AddRowWithLabel(TableLayoutPanel panel, Label label, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoSize = true;
        label.Margin = new Padding(0, 0, 6, 6);
        panel.Controls.Add(label, 0, row);
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }
}

internal sealed class MaintenanceDialogForm : Form
{
    private readonly InventoryItemEntity _item;
    private readonly MaintenanceRecordService _service;

    private readonly DataGridView _grid = new();
    private readonly Button _btnNew = new();
    private readonly Button _btnClose = new();
    private List<MaintenanceRecordEntity> _records = new();

    public MaintenanceDialogForm(InventoryItemEntity item, MaintenanceRecordService service)
    {
        _item = item;
        _service = service;
        Text = $"Manutenção — {item.Name}";
        ClientSize = new Size(700, 460);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);

        BuildLayout();
        Shown += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    private void BuildLayout()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Color.FromArgb(230, 230, 230);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        // Header styling
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(240, 240, 240),
            ForeColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI Semibold", 9f),
            Padding = new Padding(4, 4, 4, 4),
            SelectionBackColor = Color.FromArgb(240, 240, 240),
            SelectionForeColor = Color.FromArgb(60, 60, 60),
        };
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 30;

        // Row styling
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Segoe UI", 9f),
            Padding = new Padding(4, 2, 4, 2),
            SelectionBackColor = Color.FromArgb(0, 120, 215),
            SelectionForeColor = Color.White,
        };
        _grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(245, 248, 255),
        };
        _grid.RowTemplate.Height = 26;

        _grid.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { HeaderText = "Data",        Name = "colDate",   FillWeight = 15, MinimumWidth = 90  },
            new DataGridViewTextBoxColumn { HeaderText = "Tipo",        Name = "colType",   FillWeight = 14, MinimumWidth = 80  },
            new DataGridViewTextBoxColumn { HeaderText = "Status",      Name = "colStatus", FillWeight = 12, MinimumWidth = 70  },
            new DataGridViewTextBoxColumn { HeaderText = "Descrição",   Name = "colDesc",   FillWeight = 30, MinimumWidth = 120 },
            new DataGridViewTextBoxColumn { HeaderText = "Responsável", Name = "colBy",     FillWeight = 17, MinimumWidth = 90  },
            new DataGridViewTextBoxColumn { HeaderText = "Custo (R$)",  Name = "colCost",   FillWeight = 12, MinimumWidth = 70  },
        });
        DoubleBufferingHelper.EnableOptimizedDoubleBuffering(_grid);

        var panelBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(4),
        };

        _btnClose.Text = "Fechar";
        _btnClose.Size = new Size(90, 30);
        _btnClose.FlatStyle = FlatStyle.Flat;
        _btnClose.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnClose.Click += (_, _) => Close();

        _btnNew.BackColor = Color.FromArgb(0, 120, 215);
        _btnNew.FlatStyle = FlatStyle.Flat;
        _btnNew.FlatAppearance.BorderSize = 0;
        _btnNew.ForeColor = Color.White;
        _btnNew.Text = "Novo registro";
        _btnNew.Size = new Size(110, 30);
        _btnNew.UseVisualStyleBackColor = false;
        _btnNew.Click += async (_, _) => await OnNewMaintenanceAsync();

        panelBottom.Controls.Add(_btnClose);
        panelBottom.Controls.Add(_btnNew);

        Controls.Add(_grid);
        Controls.Add(panelBottom);
    }

    private async Task LoadAsync()
    {
        try
        {
            _records = (await _service.GetByItemAsync(_item.Id).ConfigureAwait(true)).ToList();
            PopulateGrid();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar manutenção: {ex.Message}", "Manutenção",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var rec in _records)
        {
            var row = _grid.Rows[_grid.Rows.Add()];
            row.Cells["colDate"].Value = (rec.CompletedAt ?? rec.ScheduledAt ?? rec.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy");
            row.Cells["colType"].Value = rec.MaintenanceType;
            row.Cells["colStatus"].Value = rec.Status;
            row.Cells["colDesc"].Value = rec.Description;
            row.Cells["colBy"].Value = rec.PerformedBy ?? string.Empty;
            row.Cells["colCost"].Value = rec.CostCents.HasValue ? (rec.CostCents.Value / 100.0).ToString("F2") : string.Empty;
            row.Tag = rec;
        }
    }

    private async Task OnNewMaintenanceAsync()
    {
        using var dlg = new MaintenanceRecordEditorForm();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result is null)
        {
            return;
        }

        try
        {
            dlg.Result.ItemId = _item.Id;
            await _service.CreateAsync(dlg.Result).ConfigureAwait(true);
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao salvar: {ex.Message}", "Manutenção",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class MaintenanceRecordEditorForm : Form
{
    private static readonly string[] MaintenanceTypes = { "Preventive", "Corrective", "Inspection" };
    private static readonly string[] StatusOptions = { "Scheduled", "InProgress", "Completed", "Cancelled" };

    private readonly ComboBox _cmbType = new();
    private readonly ComboBox _cmbStatus = new();
    private readonly TextBox _txtDescription = new();
    private readonly TextBox _txtPerformedBy = new();
    private readonly TextBox _txtNotes = new();
    private readonly NumericUpDown _numCost = new();
    private readonly CheckBox _chkScheduled = new();
    private readonly DateTimePicker _dtpScheduled = new();
    private readonly CheckBox _chkCompleted = new();
    private readonly DateTimePicker _dtpCompleted = new();
    private readonly Button _btnOk = new();
    private readonly Button _btnCancel = new();

    public MaintenanceRecordEntity? Result { get; private set; }

    public MaintenanceRecordEditorForm()
    {
        Text = "Novo Registro de Manutenção";
        MinimumSize = new Size(460, 420);
        ClientSize = new Size(480, 460);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        // Type
        _cmbType.Dock = DockStyle.Fill;
        _cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var item in MaintenanceTypes) _cmbType.Items.Add(item);
        _cmbType.SelectedIndex = 0;
        AddRow(layout, "Tipo:", _cmbType);

        // Status
        _cmbStatus.Dock = DockStyle.Fill;
        _cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var item in StatusOptions) _cmbStatus.Items.Add(item);
        _cmbStatus.SelectedIndex = 0;
        AddRow(layout, "Status:", _cmbStatus);

        // Description
        _txtDescription.Dock = DockStyle.Fill;
        AddRow(layout, "Descrição:*", _txtDescription);

        // PerformedBy
        _txtPerformedBy.Dock = DockStyle.Fill;
        AddRow(layout, "Responsável:", _txtPerformedBy);

        // Notes
        _txtNotes.Dock = DockStyle.Fill;
        _txtNotes.Multiline = true;
        _txtNotes.Height = 50;
        AddRow(layout, "Notas:", _txtNotes);

        // Cost
        _numCost.Dock = DockStyle.Left;
        _numCost.Width = 120;
        _numCost.DecimalPlaces = 2;
        _numCost.Maximum = 9999999;
        AddRow(layout, "Custo (R$):", _numCost);

        // Scheduled date
        var schedPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };
        _chkScheduled.AutoSize = true;
        _chkScheduled.Text = "Agendado para:";
        _chkScheduled.CheckedChanged += (_, _) => _dtpScheduled.Enabled = _chkScheduled.Checked;
        _dtpScheduled.Width = 140;
        _dtpScheduled.Enabled = false;
        _dtpScheduled.Margin = new Padding(4, 0, 0, 0);
        schedPanel.Controls.Add(_chkScheduled);
        schedPanel.Controls.Add(_dtpScheduled);
        AddRowSpan(layout, schedPanel);

        // Completed date
        var compPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };
        _chkCompleted.AutoSize = true;
        _chkCompleted.Text = "Concluído em:";
        _chkCompleted.CheckedChanged += (_, _) => _dtpCompleted.Enabled = _chkCompleted.Checked;
        _dtpCompleted.Width = 140;
        _dtpCompleted.Enabled = false;
        _dtpCompleted.Margin = new Padding(4, 0, 0, 0);
        compPanel.Controls.Add(_chkCompleted);
        compPanel.Controls.Add(_dtpCompleted);
        AddRowSpan(layout, compPanel);

        // Buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        _btnCancel.Text = "Cancelar";
        _btnCancel.AutoSize = false;
        _btnCancel.Size = new Size(90, 30);
        _btnCancel.FlatStyle = FlatStyle.Flat;
        _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnOk.BackColor = Color.FromArgb(0, 120, 215);
        _btnOk.FlatStyle = FlatStyle.Flat;
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.ForeColor = Color.White;
        _btnOk.Text = "Salvar";
        _btnOk.AutoSize = false;
        _btnOk.Size = new Size(90, 30);
        _btnOk.UseVisualStyleBackColor = false;
        _btnOk.Click += OnSaveClicked;

        buttonPanel.Controls.Add(_btnCancel);
        buttonPanel.Controls.Add(_btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 6),
        }, 0, row);
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private static void AddRowSpan(TableLayoutPanel panel, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, 2);
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtDescription.Text))
        {
            MessageBox.Show("A descrição é obrigatória.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtDescription.Focus();
            return;
        }

        Result = new MaintenanceRecordEntity
        {
            MaintenanceType = (_cmbType.SelectedItem as string) ?? "Corrective",
            Status = (_cmbStatus.SelectedItem as string) ?? "Completed",
            Description = _txtDescription.Text.Trim(),
            PerformedBy = string.IsNullOrWhiteSpace(_txtPerformedBy.Text) ? null : _txtPerformedBy.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim(),
            CostCents = _numCost.Value > 0 ? (long?)((long)(_numCost.Value * 100)) : null,
            ScheduledAt = _chkScheduled.Checked ? (DateTime?)_dtpScheduled.Value.ToUniversalTime() : null,
            CompletedAt = _chkCompleted.Checked ? (DateTime?)_dtpCompleted.Value.ToUniversalTime() : null,
            CreatedAt = DateTime.UtcNow,
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
