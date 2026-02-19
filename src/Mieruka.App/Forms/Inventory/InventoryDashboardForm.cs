#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;

namespace Mieruka.App.Forms.Inventory;

/// <summary>
/// Painel de dashboard/relatórios do inventário com resumos, alertas e gráficos.
/// </summary>
public sealed class InventoryDashboardForm : Form
{
    private readonly InventoryService _inventoryService;
    private readonly MaintenanceRecordService _maintenanceService;
    private readonly InventoryCategoryService _categoryService;

    public InventoryDashboardForm(
        InventoryService inventoryService,
        MaintenanceRecordService maintenanceService,
        InventoryCategoryService categoryService)
    {
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));
        _categoryService = categoryService ?? throw new ArgumentNullException(nameof(categoryService));

        BuildLayout();
        Shown += async (_, _) => await LoadDashboardAsync();
    }

    // ── Cards ──
    private readonly Label _lblTotalItems = new();
    private readonly Label _lblTotalValue = new();
    private readonly Label _lblActiveItems = new();
    private readonly Label _lblPendingMaintenance = new();

    // ── Grids ──
    private readonly DataGridView _gridCategorySummary = new();
    private readonly DataGridView _gridStatusSummary = new();
    private readonly DataGridView _gridLocationSummary = new();
    private readonly DataGridView _gridExpiringWarranty = new();
    private readonly DataGridView _gridOverdueMaintenance = new();

    private void BuildLayout()
    {
        Text = "Dashboard — Inventário";
        ClientSize = new Size(960, 700);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterParent;
        AutoScroll = true;

        var mainPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16),
        };

        // ── KPI Cards row ──
        var cardsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        cardsPanel.Controls.Add(CreateKpiCard("Total de Itens", _lblTotalItems, Color.FromArgb(0, 120, 215)));
        cardsPanel.Controls.Add(CreateKpiCard("Valor Total (R$)", _lblTotalValue, Color.FromArgb(16, 124, 16)));
        cardsPanel.Controls.Add(CreateKpiCard("Itens Ativos", _lblActiveItems, Color.FromArgb(80, 80, 80)));
        cardsPanel.Controls.Add(CreateKpiCard("Manutenção Pendente", _lblPendingMaintenance, Color.FromArgb(200, 80, 0)));
        mainPanel.Controls.Add(cardsPanel);

        // ── Summary grids row ──
        var summaryPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        summaryPanel.Controls.Add(CreateSummarySection("Por Categoria", _gridCategorySummary, 280, 200));
        summaryPanel.Controls.Add(CreateSummarySection("Por Status", _gridStatusSummary, 280, 200));
        summaryPanel.Controls.Add(CreateSummarySection("Por Localização", _gridLocationSummary, 280, 200));
        mainPanel.Controls.Add(summaryPanel);

        // ── Alerts row ──
        var alertsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };

        alertsPanel.Controls.Add(CreateSummarySection("Garantias Expirando (30 dias)", _gridExpiringWarranty, 440, 180));
        alertsPanel.Controls.Add(CreateSummarySection("Manutenções Atrasadas", _gridOverdueMaintenance, 440, 180));
        mainPanel.Controls.Add(alertsPanel);

        Controls.Add(mainPanel);
    }

    private static Panel CreateKpiCard(string title, Label valueLabel, Color accentColor)
    {
        var card = new Panel
        {
            Size = new Size(200, 80),
            Margin = new Padding(0, 0, 12, 0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
        };

        var accent = new Panel
        {
            Dock = DockStyle.Left,
            Width = 4,
            BackColor = accentColor,
        };

        var lblTitle = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(14, 8),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
            ForeColor = Color.Gray,
        };

        valueLabel.Text = "...";
        valueLabel.AutoSize = true;
        valueLabel.Location = new Point(14, 36);
        valueLabel.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
        valueLabel.ForeColor = accentColor;

        card.Controls.Add(accent);
        card.Controls.Add(lblTitle);
        card.Controls.Add(valueLabel);

        return card;
    }

    private static Panel CreateSummarySection(string title, DataGridView grid, int width, int height)
    {
        var panel = new Panel
        {
            Size = new Size(width, height + 24),
            Margin = new Padding(0, 0, 12, 12),
        };

        var lbl = new Label
        {
            Text = title,
            AutoSize = true,
            Location = new Point(0, 0),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };

        grid.Location = new Point(0, 22);
        grid.Size = new Size(width, height);
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        panel.Controls.Add(lbl);
        panel.Controls.Add(grid);

        return panel;
    }

    private async Task LoadDashboardAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;

            // KPIs
            var totalItems = await _inventoryService.CountAsync().ConfigureAwait(true);
            var totalValue = await _inventoryService.GetTotalValueAsync().ConfigureAwait(true);
            var categorySummary = await _inventoryService.GetCategorySummaryAsync().ConfigureAwait(true);
            var statusSummary = await _inventoryService.GetStatusSummaryAsync().ConfigureAwait(true);
            var locationSummary = await _inventoryService.GetLocationSummaryAsync().ConfigureAwait(true);
            var expiringWarranty = await _inventoryService.GetExpiringWarrantyAsync(30).ConfigureAwait(true);
            var overdueMaintenance = await _maintenanceService.GetOverdueAsync().ConfigureAwait(true);
            var scheduledMaintenance = await _maintenanceService.GetScheduledAsync().ConfigureAwait(true);

            _lblTotalItems.Text = totalItems.ToString("N0");
            _lblTotalValue.Text = totalValue.ToString("N2");
            _lblActiveItems.Text = statusSummary.GetValueOrDefault(InventoryItemStatus.Active, 0).ToString("N0");
            _lblPendingMaintenance.Text = (overdueMaintenance.Count + scheduledMaintenance.Count).ToString("N0");

            // Category summary
            PopulateSummaryGrid(_gridCategorySummary, "Categoria", categorySummary);

            // Status summary
            PopulateSummaryGrid(_gridStatusSummary, "Status", statusSummary);

            // Location summary
            PopulateSummaryGrid(_gridLocationSummary, "Localização", locationSummary);

            // Expiring warranty
            PopulateWarrantyGrid(expiringWarranty);

            // Overdue maintenance
            PopulateMaintenanceGrid(overdueMaintenance);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar dashboard: {ex.Message}", "Dashboard",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static void PopulateSummaryGrid(DataGridView grid, string keyHeader, IReadOnlyDictionary<string, int> data)
    {
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colKey",
            HeaderText = keyHeader,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colCount",
            HeaderText = "Qtd",
            Width = 60,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight },
        });

        grid.Rows.Clear();
        foreach (var kvp in data.OrderByDescending(x => x.Value))
        {
            var row = grid.Rows.Add();
            grid.Rows[row].Cells["colKey"].Value = kvp.Key;
            grid.Rows[row].Cells["colCount"].Value = kvp.Value;
        }
    }

    private void PopulateWarrantyGrid(IReadOnlyList<InventoryItemEntity> items)
    {
        _gridExpiringWarranty.Columns.Clear();
        _gridExpiringWarranty.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colName",
            HeaderText = "Item",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _gridExpiringWarranty.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colCategory",
            HeaderText = "Categoria",
            Width = 100,
        });
        _gridExpiringWarranty.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colExpiry",
            HeaderText = "Expira em",
            Width = 100,
        });

        _gridExpiringWarranty.Rows.Clear();
        foreach (var item in items)
        {
            var row = _gridExpiringWarranty.Rows.Add();
            _gridExpiringWarranty.Rows[row].Cells["colName"].Value = item.Name;
            _gridExpiringWarranty.Rows[row].Cells["colCategory"].Value = item.Category;
            _gridExpiringWarranty.Rows[row].Cells["colExpiry"].Value =
                item.WarrantyExpiresAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "—";
        }
    }

    private void PopulateMaintenanceGrid(IReadOnlyList<MaintenanceRecordEntity> records)
    {
        _gridOverdueMaintenance.Columns.Clear();
        _gridOverdueMaintenance.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colDescription",
            HeaderText = "Descrição",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
        });
        _gridOverdueMaintenance.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colType",
            HeaderText = "Tipo",
            Width = 90,
        });
        _gridOverdueMaintenance.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "colScheduled",
            HeaderText = "Agendada",
            Width = 100,
        });

        _gridOverdueMaintenance.Rows.Clear();
        foreach (var rec in records)
        {
            var row = _gridOverdueMaintenance.Rows.Add();
            _gridOverdueMaintenance.Rows[row].Cells["colDescription"].Value = rec.Description;
            _gridOverdueMaintenance.Rows[row].Cells["colType"].Value = rec.MaintenanceType;
            _gridOverdueMaintenance.Rows[row].Cells["colScheduled"].Value =
                rec.ScheduledAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "—";
        }
    }
}
