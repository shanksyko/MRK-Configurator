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
        ClientSize = new Size(480, 540);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;
        AutoScroll = true;

        int y = 12;

        AddField("Nome:*", _txtName, ref y);
        _txtName.MaxLength = 200;

        AddCategoryField(categories, ref y);

        AddField("Nº de Série:", _txtSerialNumber, ref y);
        AddField("Patrimônio:", _txtAssetTag, ref y);
        AddField("Fabricante:", _txtManufacturer, ref y);
        AddField("Modelo:", _txtModel, ref y);

        // Description
        var lblDesc = new Label { AutoSize = true, Location = new Point(12, y + 3), Text = "Descrição:" };
        Controls.Add(lblDesc);
        _txtDescription.Location = new Point(150, y);
        _txtDescription.Size = new Size(290, 46);
        _txtDescription.Multiline = true;
        _txtDescription.ScrollBars = ScrollBars.Vertical;
        Controls.Add(_txtDescription);
        y += 54;

        AddField("Localização:", _txtLocation, ref y);
        AddField("Responsável:", _txtAssignedTo, ref y);

        // Status
        AddLabel("Status:", ref y, out var lblStatus);
        Controls.Add(lblStatus);
        _cmbStatus.Location = new Point(150, y - 28);
        _cmbStatus.Size = new Size(290, 23);
        _cmbStatus.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var s in StatusOptions)
        {
            _cmbStatus.Items.Add(s);
        }
        _cmbStatus.SelectedIndex = 0;
        Controls.Add(_cmbStatus);

        // Quantity
        y += 6;
        AddLabel("Quantidade:", ref y, out var lblQty);
        Controls.Add(lblQty);
        _numQuantity.Location = new Point(150, y - 28);
        _numQuantity.Width = 80;
        _numQuantity.Minimum = 0;
        _numQuantity.Maximum = 9999;
        _numQuantity.Value = 1;
        Controls.Add(_numQuantity);

        // Monitor
        y += 6;
        AddLabel("Monitor vinculado:", ref y, out var lblMonitor);
        Controls.Add(lblMonitor);
        _cmbMonitor.Location = new Point(150, y - 28);
        _cmbMonitor.Size = new Size(290, 23);
        _cmbMonitor.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMonitor.Items.Add("(Nenhum)");
        foreach (var mon in monitors)
        {
            var label = string.IsNullOrWhiteSpace(mon.Name) ? mon.DeviceName : mon.Name;
            _cmbMonitor.Items.Add(new MonitorComboItem(mon.StableId ?? mon.DeviceName, label));
        }
        _cmbMonitor.SelectedIndex = 0;
        Controls.Add(_cmbMonitor);

        // Unit cost (R$)
        y += 6;
        AddLabel("Custo Unitário (R$):", ref y, out var lblCost);
        Controls.Add(lblCost);
        _numUnitCost.Location = new Point(150, y - 28);
        _numUnitCost.Width = 140;
        _numUnitCost.Minimum = 0;
        _numUnitCost.Maximum = 99_999_999;
        _numUnitCost.DecimalPlaces = 2;
        _numUnitCost.ThousandsSeparator = true;
        _numUnitCost.Value = 0;
        Controls.Add(_numUnitCost);

        // Acquired date
        y += 4;
        _chkAcquired.AutoSize = true;
        _chkAcquired.Location = new Point(12, y);
        _chkAcquired.Text = "Adquirido em:";
        _chkAcquired.CheckedChanged += (_, _) => _dtpAcquired.Enabled = _chkAcquired.Checked;
        Controls.Add(_chkAcquired);

        _dtpAcquired.Location = new Point(200, y - 2);
        _dtpAcquired.Width = 150;
        _dtpAcquired.Value = DateTime.Today;
        _dtpAcquired.Enabled = false;
        Controls.Add(_dtpAcquired);
        y += 32;

        // Warranty
        y += 10;
        _chkWarranty.AutoSize = true;
        _chkWarranty.Location = new Point(12, y);
        _chkWarranty.Text = "Garantia expira em:";
        _chkWarranty.CheckedChanged += (_, _) => _dtpWarranty.Enabled = _chkWarranty.Checked;
        Controls.Add(_chkWarranty);

        _dtpWarranty.Location = new Point(200, y - 2);
        _dtpWarranty.Width = 150;
        _dtpWarranty.Value = DateTime.Today.AddYears(1);
        _dtpWarranty.Enabled = false;
        Controls.Add(_dtpWarranty);
        y += 32;

        // Notes
        var lblNotes = new Label { AutoSize = true, Location = new Point(12, y), Text = "Notas:" };
        Controls.Add(lblNotes);
        y += 20;
        _txtNotes.Location = new Point(12, y);
        _txtNotes.Size = new Size(440, 60);
        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        Controls.Add(_txtNotes);
        y += 70;

        // Buttons
        _btnSave.BackColor = Color.FromArgb(0, 120, 215);
        _btnSave.FlatStyle = FlatStyle.Flat;
        _btnSave.ForeColor = Color.White;
        _btnSave.Location = new Point(150, y);
        _btnSave.Size = new Size(120, 30);
        _btnSave.Text = "Salvar";
        _btnSave.UseVisualStyleBackColor = false;
        _btnSave.Click += OnSaveClicked;
        Controls.Add(_btnSave);

        _btnCancel.Location = new Point(290, y);
        _btnCancel.Size = new Size(120, 30);
        _btnCancel.Text = "Cancelar";
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_btnCancel);

        ClientSize = new Size(480, y + 50);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;
    }

    private void AddField(string labelText, TextBox txt, ref int y)
    {
        var lbl = new Label { AutoSize = true, Location = new Point(12, y + 3), Text = labelText };
        Controls.Add(lbl);
        txt.Location = new Point(150, y);
        txt.Size = new Size(290, 23);
        Controls.Add(txt);
        y += 32;
    }

    private void AddCategoryField(IReadOnlyList<InventoryCategoryEntity> categories, ref int y)
    {
        var lbl = new Label { AutoSize = true, Location = new Point(12, y + 3), Text = "Categoria:" };
        Controls.Add(lbl);
        _txtCategory.Location = new Point(150, y);
        _txtCategory.Size = new Size(290, 23);

        // Use AutoComplete from existing categories
        var source = new AutoCompleteStringCollection();
        foreach (var cat in categories)
        {
            source.Add(cat.Name);
        }
        _txtCategory.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _txtCategory.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _txtCategory.AutoCompleteCustomSource = source;

        Controls.Add(_txtCategory);
        y += 32;
    }

    private static void AddLabel(string text, ref int y, out Label lbl)
    {
        lbl = new Label { AutoSize = true, Location = new Point(12, y + 3), Text = text };
        y += 32;
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
