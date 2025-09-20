using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Layouts;

namespace Mieruka.App.Ui;

internal sealed class PresetSelectionDialog : Form
{
    private readonly IReadOnlyList<ZonePreset> _presets;
    private readonly ComboBox _presetSelector;
    private readonly ListBox _zoneList;
    private readonly Button _confirmButton;
    private readonly Button _cancelButton;

    public PresetSelectionDialog(IEnumerable<ZonePreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        _presets = presets
            .Select(static preset => preset ?? throw new ArgumentException("A lista de presets não pode conter valores nulos.", nameof(presets)))
            .ToList();

        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        Text = "Selecionar Preset";
        ClientSize = new Size(420, 360);

        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        _presetSelector = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(12),
            Font = baseFont,
        };

        _zoneList = new ListBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12),
            Font = baseFont,
        };

        _confirmButton = new Button
        {
            Text = "Aplicar",
            DialogResult = DialogResult.OK,
            Enabled = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 8, 0),
        };

        _cancelButton = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(8, 0, 8, 0),
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        footer.Controls.Add(_cancelButton);
        footer.Controls.Add(_confirmButton);

        Controls.Add(_zoneList);
        Controls.Add(_presetSelector);
        Controls.Add(footer);

        _presetSelector.SelectedIndexChanged += (_, _) => PopulateZones();
        _zoneList.SelectedIndexChanged += (_, _) => UpdateConfirmState();
        _zoneList.DoubleClick += (_, _) => ConfirmSelection();
        _confirmButton.Click += (_, _) => ConfirmSelection();

        AcceptButton = _confirmButton;
        CancelButton = _cancelButton;

        PopulatePresets();
    }

    public ZonePreset? SelectedPreset { get; private set; }

    public ZonePreset.Zone? SelectedZone { get; private set; }

    private void PopulatePresets()
    {
        _presetSelector.Items.Clear();

        foreach (var preset in _presets)
        {
            _presetSelector.Items.Add(new PresetOption(preset));
        }

        if (_presetSelector.Items.Count > 0)
        {
            _presetSelector.SelectedIndex = 0;
        }
    }

    private void PopulateZones()
    {
        _zoneList.Items.Clear();

        if (_presetSelector.SelectedItem is not PresetOption option)
        {
            UpdateConfirmState();
            return;
        }

        foreach (var zone in option.Preset.Zones)
        {
            _zoneList.Items.Add(new ZoneOption(zone));
        }

        if (_zoneList.Items.Count > 0)
        {
            _zoneList.SelectedIndex = 0;
        }

        UpdateConfirmState();
    }

    private void UpdateConfirmState()
    {
        _confirmButton.Enabled = _zoneList.SelectedItem is ZoneOption;
    }

    private void ConfirmSelection()
    {
        if (_zoneList.SelectedItem is not ZoneOption zoneOption || _presetSelector.SelectedItem is not PresetOption presetOption)
        {
            DialogResult = DialogResult.None;
            return;
        }

        SelectedPreset = presetOption.Preset;
        SelectedZone = zoneOption.Zone;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record class PresetOption(ZonePreset Preset)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Preset.Name) ? Preset.Id : Preset.Name;
    }

    private sealed record class ZoneOption(ZonePreset.Zone Zone)
    {
        public override string ToString()
        {
            var label = string.IsNullOrWhiteSpace(Zone.Id) ? "Zona" : Zone.Id;
            return $"{label} ({Zone.WidthPercentage:0.#}% x {Zone.HeightPercentage:0.#}%)";
        }
    }
}
