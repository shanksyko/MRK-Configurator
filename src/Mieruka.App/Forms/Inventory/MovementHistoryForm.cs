#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;

namespace Mieruka.App.Forms.Inventory;

public sealed class MovementHistoryForm : Form
{
    private readonly InventoryMovementService _movementService;
    private readonly int _itemId;
    private readonly string _itemName;

    private readonly ListView _listView = new();
    private readonly Button _btnClose = new();

    public MovementHistoryForm(int itemId, string itemName, InventoryMovementService movementService)
    {
        _itemId = itemId;
        _itemName = itemName ?? string.Empty;
        _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
        BuildLayout();
        Shown += async (_, _) => await LoadAsync();
    }

    private void BuildLayout()
    {
        Text = $"Histórico de Movimentações — {_itemName}";
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(640, 400);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 9f);

        _listView.Dock = DockStyle.Fill;
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.GridLines = true;
        _listView.MultiSelect = false;
        _listView.BorderStyle = BorderStyle.None;

        _listView.Columns.AddRange(new[]
        {
            new ColumnHeader { Text = "Data/Hora",       Width = 140 },
            new ColumnHeader { Text = "Tipo",             Width = 100 },
            new ColumnHeader { Text = "De (Local)",       Width = 120 },
            new ColumnHeader { Text = "Para (Local)",     Width = 120 },
            new ColumnHeader { Text = "De (Responsável)", Width = 120 },
            new ColumnHeader { Text = "Para (Responsável)", Width = 120 },
            new ColumnHeader { Text = "Realizado por",   Width = 120 },
            new ColumnHeader { Text = "Notas",           Width = -2 },
        });

        var panelBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(6),
        };

        _btnClose.Text = "Fechar";
        _btnClose.Size = new Size(90, 30);
        _btnClose.FlatStyle = FlatStyle.Flat;
        _btnClose.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        _btnClose.Click += (_, _) => Close();
        panelBottom.Controls.Add(_btnClose);

        Controls.Add(_listView);
        Controls.Add(panelBottom);

        AcceptButton = _btnClose;
        CancelButton = _btnClose;
    }

    private async Task LoadAsync()
    {
        try
        {
            var movements = await _movementService.GetMovementHistoryAsync(_itemId).ConfigureAwait(true);
            PopulateList(movements);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao carregar histórico: {ex.Message}", "Histórico",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PopulateList(IReadOnlyList<InventoryMovementEntity> movements)
    {
        _listView.Items.Clear();
        foreach (var m in movements)
        {
            var item = new ListViewItem(m.MovedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            item.SubItems.Add(m.MovementType);
            item.SubItems.Add(m.FromLocation ?? string.Empty);
            item.SubItems.Add(m.ToLocation ?? string.Empty);
            item.SubItems.Add(m.FromAssignee ?? string.Empty);
            item.SubItems.Add(m.ToAssignee ?? string.Empty);
            item.SubItems.Add(m.PerformedBy ?? string.Empty);
            item.SubItems.Add(m.Notes ?? string.Empty);
            item.Tag = m;
            _listView.Items.Add(item);
        }
    }
}
