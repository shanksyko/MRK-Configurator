#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Mieruka.Core.Data.Entities;
using Mieruka.Core.Data.Services;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms.Inventory;

public sealed class InventoryItemEditorForm : Form
{
    private static readonly string[] StatusOptions = InventoryItemStatus.All;

    private readonly InventoryItemEntity? _existing;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtCategory = new();
    private readonly TextBox _txtSerialNumber = new();
    private readonly TextBox _txtAssetTag = new();
    private readonly TextBox _txtManufacturer = new();
    private readonly TextBox _txtModel = new();
    private readonly TextBox _txtLocation = new();
    private readonly TextBox _txtAssignedTo = new();
    private readonly ComboBox _cmbStatus = new();
    private readonly NumericUpDown _numQuantity = new();
    private readonly ComboBox _cmbMonitor = new();
    private readonly CheckBox _chkWarranty = new();
    private readonly DateTimePicker _dtpWarranty = new();
    private readonly TextBox _txtNotes = new();
    private readonly NumericUpDown _numUnitCost = new();
    private readonly CheckBox _chkAcquired = new();
    private readonly DateTimePicker _dtpAcquired = new();
    private readonly TextBox _txtDescription = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    public InventoryItemEntity? Result { get; private set; }

    public InventoryItemEditorForm(
        InventoryItemEntity? existing,
        IReadOnlyList<InventoryCategoryEntity> categories,
        IReadOnlyList<MonitorInfo>? monitors = null)
    {
        _existing = existing;
        BuildLayout(categories, monitors ?? Array.Empty<MonitorInfo>());
        if (existing is not null)
        {
            FillValues(existing);
        }
    }

    private void BuildLayout(IReadOnlyList<InventoryCategoryEntity> categories, IReadOnlyList<MonitorInfo> monitors)
    {
        Text = _existing is null ? "Novo Item" : "Editar Item";
        MinimumSize = new Size(500, 580);
        ClientSize = new Size(520, 620);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        _txtName.Dock = DockStyle.Fill;
        _txtName.MaxLength = 200;
        AddRow(layout, "Nome:*", _txtName);

        // Category with AutoComplete
        _txtCategory.Dock = DockStyle.Fill;
        var source = new AutoCompleteStringCollection();
        foreach (var cat in categories) source.Add(cat.Name);
        _txtCategory.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _txtCategory.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _txtCategory.AutoCompleteCustomSource = source;
        AddRow(layout, "Categoria:", _txtCategory);

        _txtSerialNumber.Dock = DockStyle.Fill;
        AddRow(layout, "Nº de Série:", _txtSerialNumber);

        _txtAssetTag.Dock = DockStyle.Fill;
        AddRow(layout, "Patrimônio:", _txtAssetTag);

        _txtManufacturer.Dock = DockStyle.Fill;
        AddRow(layout, "Fabricante:", _txtManufacturer);

        _txtModel.Dock = DockStyle.Fill;
        AddRow(layout, "Modelo:", _txtModel);

        _txtDescription.Dock = DockStyle.Fill;
        _txtDescription.Multiline = true;
        _txtDescription.Height = 46;
        _txtDescription.ScrollBars = ScrollBars.Vertical;
        AddRow(layout, "Descrição:", _txtDescription);

        _txtLocation.Dock = DockStyle.Fill;
        AddRow(layout, "Localização:", _txtLocation);

        _txtAssignedTo.Dock = DockStyle.Fill;
        AddRow(layout, "Responsável:", _txtAssignedTo);

        _cmbStatus.Dock = DockStyle.Fill;
        _cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var s in StatusOptions) _cmbStatus.Items.Add(s);
        _cmbStatus.SelectedIndex = 0;
        AddRow(layout, "Status:", _cmbStatus);

        _numQuantity.Dock = DockStyle.Left;
        _numQuantity.Width = 80;
        _numQuantity.Minimum = 0;
        _numQuantity.Maximum = 9999;
        _numQuantity.Value = 1;
        AddRow(layout, "Quantidade:", _numQuantity);

        _cmbMonitor.Dock = DockStyle.Fill;
        _cmbMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMonitor.Items.Add("(Nenhum)");
        foreach (var mon in monitors)
        {
            var label = string.IsNullOrWhiteSpace(mon.Name) ? mon.DeviceName : mon.Name;
            _cmbMonitor.Items.Add(new MonitorComboItem(mon.StableId ?? mon.DeviceName, label));
        }
        _cmbMonitor.SelectedIndex = 0;
        AddRow(layout, "Monitor vinculado:", _cmbMonitor);

        _numUnitCost.Dock = DockStyle.Left;
        _numUnitCost.Width = 140;
        _numUnitCost.Minimum = 0;
        _numUnitCost.Maximum = 99_999_999;
        _numUnitCost.DecimalPlaces = 2;
        _numUnitCost.ThousandsSeparator = true;
        _numUnitCost.Value = 0;
        AddRow(layout, "Custo Unitário (R$):", _numUnitCost);

        // Acquired date — checkbox + picker side-by-side
        var acquiredPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        _chkAcquired.AutoSize = true;
        _chkAcquired.Text = "Sim";
        _chkAcquired.CheckedChanged += (_, _) => _dtpAcquired.Enabled = _chkAcquired.Checked;
        _dtpAcquired.Width = 150;
        _dtpAcquired.Value = DateTime.Today;
        _dtpAcquired.Enabled = false;
        _dtpAcquired.Margin = new Padding(6, 0, 0, 0);
        acquiredPanel.Controls.Add(_chkAcquired);
        acquiredPanel.Controls.Add(_dtpAcquired);
        AddRow(layout, "Adquirido em:", acquiredPanel);

        // Warranty — checkbox + picker side-by-side
        var warrantyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        _chkWarranty.AutoSize = true;
        _chkWarranty.Text = "Sim";
        _chkWarranty.CheckedChanged += (_, _) => _dtpWarranty.Enabled = _chkWarranty.Checked;
        _dtpWarranty.Width = 150;
        _dtpWarranty.Value = DateTime.Today.AddYears(1);
        _dtpWarranty.Enabled = false;
        _dtpWarranty.Margin = new Padding(6, 0, 0, 0);
        warrantyPanel.Controls.Add(_chkWarranty);
        warrantyPanel.Controls.Add(_dtpWarranty);
        AddRow(layout, "Garantia expira em:", warrantyPanel);

        _txtNotes.Dock = DockStyle.Fill;
        _txtNotes.Multiline = true;
        _txtNotes.Height = 60;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        AddRow(layout, "Notas:", _txtNotes);

        // Button bar
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        _btnCancel.Text = "Cancelar";
        _btnCancel.AutoSize = true;
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _btnSave.BackColor = Color.FromArgb(0, 120, 215);
        _btnSave.FlatStyle = FlatStyle.Flat;
        _btnSave.ForeColor = Color.White;
        _btnSave.Text = "Salvar";
        _btnSave.AutoSize = true;
        _btnSave.UseVisualStyleBackColor = false;
        _btnSave.Click += OnSaveClicked;
        buttonPanel.Controls.Add(_btnCancel);
        buttonPanel.Controls.Add(_btnSave);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = _btnSave;
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

    private void FillValues(InventoryItemEntity item)
    {
        _txtName.Text = item.Name;
        _txtCategory.Text = item.Category;
        _txtSerialNumber.Text = item.SerialNumber ?? string.Empty;
        _txtAssetTag.Text = item.AssetTag ?? string.Empty;
        _txtManufacturer.Text = item.Manufacturer ?? string.Empty;
        _txtModel.Text = item.Model ?? string.Empty;
        _txtDescription.Text = item.Description ?? string.Empty;
        _txtLocation.Text = item.Location ?? string.Empty;
        _txtAssignedTo.Text = item.AssignedTo ?? string.Empty;
        _txtNotes.Text = item.Notes ?? string.Empty;
        _numQuantity.Value = Math.Max(0, Math.Min(9999, item.Quantity));

        if (item.UnitCostCents.HasValue)
        {
            _numUnitCost.Value = Math.Min(99_999_999m, item.UnitCostCents.Value / 100m);
        }

        if (item.AcquiredAt.HasValue)
        {
            _chkAcquired.Checked = true;
            _dtpAcquired.Value = item.AcquiredAt.Value.ToLocalTime();
        }

        var statusIdx = Array.IndexOf(StatusOptions, item.Status);
        _cmbStatus.SelectedIndex = statusIdx >= 0 ? statusIdx : 0;

        if (!string.IsNullOrWhiteSpace(item.LinkedMonitorStableId))
        {
            for (var i = 1; i < _cmbMonitor.Items.Count; i++)
            {
                if (_cmbMonitor.Items[i] is MonitorComboItem mci &&
                    string.Equals(mci.StableId, item.LinkedMonitorStableId, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbMonitor.SelectedIndex = i;
                    break;
                }
            }
        }

        if (item.WarrantyExpiresAt.HasValue)
        {
            _chkWarranty.Checked = true;
            _dtpWarranty.Value = item.WarrantyExpiresAt.Value.ToLocalTime();
        }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        {
            MessageBox.Show("O campo 'Nome' é obrigatório.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtName.Focus();
            return;
        }

        var item = _existing ?? new InventoryItemEntity();

        // Populate all fields directly.
        item.Name = _txtName.Text.Trim();
        item.Category = _txtCategory.Text.Trim();
        item.Description = string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text.Trim();
        item.SerialNumber = string.IsNullOrWhiteSpace(_txtSerialNumber.Text) ? null : _txtSerialNumber.Text.Trim();
        item.AssetTag = string.IsNullOrWhiteSpace(_txtAssetTag.Text) ? null : _txtAssetTag.Text.Trim();
        item.Manufacturer = string.IsNullOrWhiteSpace(_txtManufacturer.Text) ? null : _txtManufacturer.Text.Trim();
        item.Model = string.IsNullOrWhiteSpace(_txtModel.Text) ? null : _txtModel.Text.Trim();
        item.Location = string.IsNullOrWhiteSpace(_txtLocation.Text) ? null : _txtLocation.Text.Trim();
        item.AssignedTo = string.IsNullOrWhiteSpace(_txtAssignedTo.Text) ? null : _txtAssignedTo.Text.Trim();
        item.Status = (_cmbStatus.SelectedItem as string) ?? "Active";
        item.Quantity = (int)_numQuantity.Value;
        item.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
        item.UnitCostCents = _numUnitCost.Value > 0 ? (long)(_numUnitCost.Value * 100) : null;
        item.AcquiredAt = _chkAcquired.Checked ? (DateTime?)_dtpAcquired.Value.ToUniversalTime() : null;
        item.UpdatedAt = DateTime.UtcNow;

        if (_existing is null)
        {
            item.CreatedAt = DateTime.UtcNow;
        }

        item.LinkedMonitorStableId = _cmbMonitor.SelectedItem is MonitorComboItem selected
            ? selected.StableId
            : null;

        item.WarrantyExpiresAt = _chkWarranty.Checked
            ? (DateTime?)_dtpWarranty.Value.ToUniversalTime()
            : null;

        Result = item;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record MonitorComboItem(string StableId, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
