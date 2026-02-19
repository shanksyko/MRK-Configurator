using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Services;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Ui;

internal sealed class AppEditorDialog : WinForms.Form
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly IReadOnlyList<ZonePreset> _zonePresets;
    private readonly List<MonitorOption> _monitorOptions = new();
    private readonly List<ZoneOption> _zoneOptions = new();
    private readonly WinForms.TextBox _nameBox;
    private readonly WinForms.TextBox _titleBox;
    private readonly WinForms.TextBox _pathBox;
    private readonly WinForms.TextBox _argsBox;
    private readonly WinForms.ComboBox _monitorBox;
    private readonly WinForms.ComboBox _zoneBox;
    private readonly WinForms.CheckBox _topMostCheck;
    private readonly WinForms.Button _testButton;

    private bool _suppressMonitorEvents;

    public AppEditorDialog(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<ZonePreset> zonePresets,
        AppConfig? template = null,
        string? selectedMonitorStableId = null)
    {
        _monitors = monitors ?? Array.Empty<MonitorInfo>();
        _zonePresets = zonePresets ?? Array.Empty<ZonePreset>();

        AutoScaleMode = WinForms.AutoScaleMode.Dpi;
        AutoScaleDimensions = new Drawing.SizeF(96f, 96f);
        MinimumSize = new Drawing.Size(900, 600);
        StartPosition = WinForms.FormStartPosition.CenterParent;
        FormBorderStyle = WinForms.FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Text = template is null ? "Adicionar Aplicativo" : "Editar Aplicativo";

        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var layout = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 2,
            Padding = new WinForms.Padding(16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 28f));
        layout.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 72f));

        _nameBox = CreateTextBox(baseFont);
        _titleBox = CreateTextBox(baseFont);
        _pathBox = CreateTextBox(baseFont);
        _argsBox = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            Height = 80,
            ScrollBars = ScrollBars.Vertical,
        };

        _monitorBox = new WinForms.ComboBox
        {
            Dock = WinForms.DockStyle.Fill,
            DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
            Font = baseFont,
        };
        _monitorBox.DisplayMember = nameof(MonitorOption.DisplayName);
        _monitorBox.ValueMember = nameof(MonitorOption.StableId);
        _monitorBox.SelectedIndexChanged += (_, _) => PopulateZones(GetSelectedZoneIdentifier());
        _monitorBox.SelectedValueChanged += OnMonitorSelectionChanged;

        _zoneBox = new WinForms.ComboBox
        {
            Dock = WinForms.DockStyle.Fill,
            DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
            Font = baseFont,
        };

        _topMostCheck = new WinForms.CheckBox
        {
            Text = "Sempre no topo",
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
        };

        AddRow(layout, "Nome", _nameBox);
        AddRow(layout, "Título da janela", _titleBox);

        var pathPanel = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        pathPanel.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 100f));
        pathPanel.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.AutoSize));
        pathPanel.Controls.Add(_pathBox, 0, 0);
        var browseButton = new WinForms.Button
        {
            Text = "...",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new WinForms.Padding(6, 0, 0, 0),
        };
        browseButton.Click += OnBrowseClicked;
        pathPanel.Controls.Add(browseButton, 1, 0);
        AddRow(layout, "Executável", pathPanel);

        AddRow(layout, "Argumentos", _argsBox);
        AddRow(layout, "Monitor", _monitorBox);
        AddRow(layout, "Zona", _zoneBox);
        AddRow(layout, string.Empty, _topMostCheck);

        var buttonPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new WinForms.Padding(16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var okButton = new WinForms.Button
        {
            Text = "Salvar",
            DialogResult = WinForms.DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        okButton.Click += OnSaveRequested;

        var cancelButton = new WinForms.Button
        {
            Text = "Cancelar",
            DialogResult = WinForms.DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        _testButton = new WinForms.Button
        {
            Text = "Testar",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        _testButton.Click += OnTestClicked;

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(_testButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        PopulateMonitors(template?.TargetMonitorStableId, template?.Window.Monitor, selectedMonitorStableId);
        PopulateZones(template?.TargetZonePresetId);
        LoadTemplate(template);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<AppConfig, Task<bool>>? TestHandler { get; set; }

    public AppConfig? Result { get; private set; }

    public event EventHandler<string?>? MonitorSelectionChanged;

    public string? SelectedMonitorStableId => GetSelectedMonitorStableId();

    public void SetSelectedMonitorStableId(string? stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return;
        }

        var match = _monitorOptions.FirstOrDefault(option =>
            !option.IsPlaceholder && string.Equals(option.StableId, stableId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        try
        {
            _suppressMonitorEvents = true;
            _monitorBox.SelectedItem = match;
        }
        finally
        {
            _suppressMonitorEvents = false;
        }
    }

    private static WinForms.TextBox CreateTextBox(Drawing.Font baseFont)
    {
        return new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
        };
    }

    private static void AddRow(WinForms.TableLayoutPanel panel, string caption, WinForms.Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));

        if (!string.IsNullOrEmpty(caption))
        {
            var label = new WinForms.Label
            {
                Text = caption,
                Dock = WinForms.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 6, 6),
            };
            panel.Controls.Add(label, 0, row);
        }
        else
        {
            panel.Controls.Add(new WinForms.Label { AutoSize = true }, 0, row);
        }

        control.Margin = new WinForms.Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private void PopulateMonitors(string? stableId, MonitorKey? fallback, string? preferredStableId)
    {
        _monitorOptions.Clear();

        foreach (var monitor in _monitors)
        {
            var name = string.IsNullOrWhiteSpace(monitor.Name)
                ? $"Monitor {monitor.Key.DisplayIndex + 1}"
                : monitor.Name;
            var displayName = $"{name} ({monitor.Width}x{monitor.Height} - {(monitor.Scale > 0 ? monitor.Scale : 1):P0})";
            var stable = WindowPlacementHelper.ResolveStableId(monitor);
            _monitorOptions.Add(new MonitorOption(stable, displayName, monitor, false));
        }

        if (_monitorOptions.Count == 0)
        {
            _monitorOptions.Add(MonitorOption.Placeholder());
            _monitorBox.Enabled = false;
        }
        else
        {
            _monitorBox.Enabled = true;
        }

        try
        {
            _suppressMonitorEvents = true;
            _monitorBox.DataSource = null;
            _monitorBox.DataSource = _monitorOptions;

            if (!SelectMonitorByStableId(preferredStableId) &&
                !SelectMonitorByStableId(stableId) &&
                !SelectMonitorByKey(fallback))
            {
                SelectFirstMonitor();
            }
        }
        finally
        {
            _suppressMonitorEvents = false;
        }
    }

    private void PopulateZones(string? selectedIdentifier)
    {
        _zoneOptions.Clear();
        _zoneBox.Items.Clear();

        foreach (var preset in _zonePresets)
        {
            foreach (var zone in preset.Zones)
            {
                var identifier = string.IsNullOrWhiteSpace(zone.Id)
                    ? preset.Id
                    : $"{preset.Id}:{zone.Id}";
                var label = string.IsNullOrWhiteSpace(zone.Id)
                    ? preset.Name
                    : $"{preset.Name} - {zone.Id}";
                var isFull = Math.Abs(zone.WidthPercentage - 100d) < 0.01 && Math.Abs(zone.HeightPercentage - 100d) < 0.01 &&
                    Math.Abs(zone.LeftPercentage) < 0.01 && Math.Abs(zone.TopPercentage) < 0.01;
                var option = new ZoneOption(identifier, label, preset.Id, zone, isFull);
                _zoneOptions.Add(option);
                _zoneBox.Items.Add(option);
            }
        }

        if (_zoneBox.Items.Count == 0)
        {
            _zoneBox.Items.Add("Nenhuma zona disponível");
            _zoneBox.SelectedIndex = 0;
            _zoneBox.Enabled = false;
            return;
        }

        _zoneBox.Enabled = true;

        if (!string.IsNullOrWhiteSpace(selectedIdentifier))
        {
            var match = _zoneOptions.FirstOrDefault(option =>
                string.Equals(option.Identifier, selectedIdentifier, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _zoneBox.SelectedItem = match;
                return;
            }
        }

        _zoneBox.SelectedIndex = 0;
    }

    private string? GetSelectedZoneIdentifier()
        => _zoneBox.SelectedItem is ZoneOption option ? option.Identifier : null;

    private void LoadTemplate(AppConfig? template)
    {
        if (template is null)
        {
            return;
        }

        _nameBox.Text = template.Id;
        _titleBox.Text = template.Window.Title;
        _pathBox.Text = template.ExecutablePath;
        _argsBox.Text = template.Arguments ?? string.Empty;
        _topMostCheck.Checked = template.Window.AlwaysOnTop;

        if (!string.IsNullOrWhiteSpace(template.TargetZonePresetId))
        {
            var match = _zoneOptions.FirstOrDefault(option =>
                string.Equals(option.Identifier, template.TargetZonePresetId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                _zoneBox.SelectedItem = match;
                return;
            }
        }

        var monitor = ResolveSelectedMonitor();
        var zoneRect = WindowPlacementHelper.CreateZoneFromWindow(template.Window, monitor);
        var inferred = _zoneOptions.FirstOrDefault(option => ZoneMatches(option.Zone, zoneRect));
        if (inferred is not null)
        {
            _zoneBox.SelectedItem = inferred;
        }
    }

    private async void OnTestClicked(object? sender, EventArgs e)
    {
        if (TestHandler is null)
        {
            return;
        }

        if (!ValidateInputs())
        {
            return;
        }

        var config = CreateConfigFromInputs();

        try
        {
            _testButton.Enabled = false;
            await TestHandler(config).ConfigureAwait(true);
        }
        finally
        {
            _testButton.Enabled = true;
        }
    }

    private void OnSaveRequested(object? sender, EventArgs e)
    {
        if (!ValidateInputs())
        {
            DialogResult = WinForms.DialogResult.None;
            return;
        }

        Result = CreateConfigFromInputs();
    }

    private void OnMonitorSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressMonitorEvents)
        {
            return;
        }

        MonitorSelectionChanged?.Invoke(this, GetSelectedMonitorStableId());
    }

    private AppConfig CreateConfigFromInputs()
    {
        var monitor = ResolveSelectedMonitor();
        var window = BuildWindowConfig(monitor);
        var zoneIdentifier = GetSelectedZoneIdentifier();

        return new AppConfig
        {
            Id = _nameBox.Text.Trim(),
            ExecutablePath = _pathBox.Text.Trim(),
            Arguments = string.IsNullOrWhiteSpace(_argsBox.Text) ? null : _argsBox.Text.Trim(),
            Window = window with { Title = _titleBox.Text?.Trim() ?? string.Empty },
            TargetMonitorStableId = WindowPlacementHelper.ResolveStableId(monitor),
            TargetZonePresetId = zoneIdentifier,
        };
    }

    private MonitorInfo ResolveSelectedMonitor()
    {
        if (_monitorBox.SelectedItem is MonitorOption option)
        {
            return option.Monitor;
        }

        return _monitors.FirstOrDefault() ?? new MonitorInfo();
    }

    private WindowConfig BuildWindowConfig(MonitorInfo monitor)
    {
        if (_zoneBox.SelectedItem is ZoneOption option)
        {
            if (option.IsFull)
            {
                return new WindowConfig
                {
                    Monitor = monitor.Key,
                    FullScreen = true,
                    AlwaysOnTop = _topMostCheck.Checked,
                };
            }

            var zoneRect = WindowPlacementHelper.ZoneRect.FromZone(option.Zone);
            var relative = CalculateRelativeRectangle(zoneRect, monitor);

            return new WindowConfig
            {
                Monitor = monitor.Key,
                X = relative.X,
                Y = relative.Y,
                Width = relative.Width,
                Height = relative.Height,
                FullScreen = false,
                AlwaysOnTop = _topMostCheck.Checked,
            };
        }

        return new WindowConfig
        {
            Monitor = monitor.Key,
            FullScreen = true,
            AlwaysOnTop = _topMostCheck.Checked,
        };
    }

    private static Drawing.Rectangle CalculateRelativeRectangle(WindowPlacementHelper.ZoneRect zone, MonitorInfo monitor)
    {
        var monitorBounds = WindowPlacementHelper.GetMonitorBounds(monitor);
        var monitorWidth = Math.Max(1, monitorBounds.Width > 0 ? monitorBounds.Width : monitor.Width);
        var monitorHeight = Math.Max(1, monitorBounds.Height > 0 ? monitorBounds.Height : monitor.Height);

        var width = Math.Max(1, (int)Math.Round(monitorWidth * (zone.WidthPercentage / 100d), MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(monitorHeight * (zone.HeightPercentage / 100d), MidpointRounding.AwayFromZero));
        var x = (int)Math.Round(monitorWidth * (zone.LeftPercentage / 100d), MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(monitorHeight * (zone.TopPercentage / 100d), MidpointRounding.AwayFromZero);

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

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Filter = "Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*",
            Title = "Selecionar executável",
        };

        if (dialog.ShowDialog(this) == WinForms.DialogResult.OK)
        {
            _pathBox.Text = dialog.FileName;
        }
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            WinForms.MessageBox.Show(this, "Informe um nome para o aplicativo.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _nameBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            WinForms.MessageBox.Show(this, "Informe o caminho do executável.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _pathBox.Focus();
            return false;
        }

        if (!File.Exists(_pathBox.Text))
        {
            var answer = WinForms.MessageBox.Show(
                this,
                "O executável informado não foi encontrado. Deseja continuar assim mesmo?",
                "Arquivo não encontrado",
                WinForms.MessageBoxButtons.YesNo,
                WinForms.MessageBoxIcon.Question);

            if (answer != WinForms.DialogResult.Yes)
            {
                _pathBox.Focus();
                return false;
            }
        }

        if (_zoneBox.Enabled && _zoneBox.SelectedItem is not ZoneOption)
        {
            WinForms.MessageBox.Show(this, "Selecione uma zona para posicionar o aplicativo.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _zoneBox.Focus();
            return false;
        }

        return true;
    }

    private static bool ZoneMatches(ZonePreset.Zone zone, WindowPlacementHelper.ZoneRect rect)
    {
        var tolerance = 0.5d;
        return Math.Abs(zone.LeftPercentage - rect.LeftPercentage) < tolerance
            && Math.Abs(zone.TopPercentage - rect.TopPercentage) < tolerance
            && Math.Abs(zone.WidthPercentage - rect.WidthPercentage) < tolerance
            && Math.Abs(zone.HeightPercentage - rect.HeightPercentage) < tolerance;
    }

    private static bool MonitorKeysEqual(MonitorKey left, MonitorKey? right)
    {
        if (right is null)
        {
            return false;
        }

        return string.Equals(left.DeviceId, right.Value.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.Value.DisplayIndex
            && left.AdapterLuidHigh == right.Value.AdapterLuidHigh
            && left.AdapterLuidLow == right.Value.AdapterLuidLow
            && left.TargetId == right.Value.TargetId;
    }

    private bool SelectMonitorByStableId(string? stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        var match = _monitorOptions.FirstOrDefault(option =>
            !option.IsPlaceholder && string.Equals(option.StableId, stableId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        _monitorBox.SelectedItem = match;
        return true;
    }

    private bool SelectMonitorByKey(MonitorKey? key)
    {
        if (key is null)
        {
            return false;
        }

        var match = _monitorOptions.FirstOrDefault(option =>
            !option.IsPlaceholder && MonitorKeysEqual(option.Monitor.Key, key));
        if (match is null)
        {
            return false;
        }

        _monitorBox.SelectedItem = match;
        return true;
    }

    private void SelectFirstMonitor()
    {
        if (_monitorOptions.Count == 0)
        {
            return;
        }

        _monitorBox.SelectedItem = _monitorOptions[0];
    }

    private string? GetSelectedMonitorStableId()
    {
        return _monitorBox.SelectedItem is MonitorOption option && !option.IsPlaceholder
            ? option.StableId
            : null;
    }

    private sealed record class MonitorOption(string StableId, string DisplayName, MonitorInfo Monitor, bool IsPlaceholder)
    {
        public override string ToString() => DisplayName;

        public static MonitorOption Placeholder()
            => new(string.Empty, "Sem monitores disponíveis", new MonitorInfo(), true);
    }

    private sealed record class ZoneOption(string Identifier, string DisplayName, string PresetId, ZonePreset.Zone Zone, bool IsFull)
    {
        public override string ToString() => DisplayName;
    }
}
