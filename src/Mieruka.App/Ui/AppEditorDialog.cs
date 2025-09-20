using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Services;
using Mieruka.Core.Models;

namespace Mieruka.App.Ui;

internal sealed class AppEditorDialog : Form
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly TextBox _nameBox;
    private readonly TextBox _titleBox;
    private readonly TextBox _pathBox;
    private readonly TextBox _argsBox;
    private readonly ComboBox _monitorBox;
    private readonly CheckBox _fullScreenCheck;
    private readonly NumericUpDown _xBox;
    private readonly NumericUpDown _yBox;
    private readonly NumericUpDown _widthBox;
    private readonly NumericUpDown _heightBox;
    private readonly CheckBox _topMostCheck;

    public AppEditorDialog(IReadOnlyList<MonitorInfo> monitors, AppConfig? template = null)
    {
        _monitors = monitors ?? Array.Empty<MonitorInfo>();

        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Text = template is null ? "Adicionar Aplicativo" : "Editar Aplicativo";

        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        _nameBox = CreateTextBox(baseFont);
        _titleBox = CreateTextBox(baseFont);
        _pathBox = CreateTextBox(baseFont);
        _argsBox = CreateTextBox(baseFont);

        _monitorBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = baseFont,
        };

        _fullScreenCheck = new CheckBox
        {
            Text = "Tela cheia",
            Dock = DockStyle.Fill,
            Checked = true,
            Font = baseFont,
        };
        _fullScreenCheck.CheckedChanged += (_, _) => UpdateWindowFieldsState();

        _xBox = CreateNumericBox();
        _yBox = CreateNumericBox();
        _widthBox = CreateNumericBox(minimum: 100, defaultValue: 800);
        _heightBox = CreateNumericBox(minimum: 100, defaultValue: 600);
        _topMostCheck = new CheckBox
        {
            Text = "Sempre no topo",
            Dock = DockStyle.Fill,
            Font = baseFont,
        };

        AddRow(layout, "Nome", _nameBox);
        AddRow(layout, "Título da janela", _titleBox);

        var pathPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathPanel.Controls.Add(_pathBox, 0, 0);
        var browseButton = new Button
        {
            Text = "...",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(6, 0, 0, 0),
        };
        browseButton.Click += OnBrowseClicked;
        pathPanel.Controls.Add(browseButton, 1, 0);

        AddRow(layout, "Executável", pathPanel);
        AddRow(layout, "Argumentos", _argsBox);
        AddRow(layout, "Monitor", _monitorBox);
        AddRow(layout, string.Empty, _fullScreenCheck);

        var positionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        positionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        positionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        positionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        positionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        positionPanel.Controls.Add(CreateLabeledNumeric("X", _xBox, baseFont), 0, 0);
        positionPanel.Controls.Add(CreateLabeledNumeric("Y", _yBox, baseFont), 1, 0);
        positionPanel.Controls.Add(CreateLabeledNumeric("Largura", _widthBox, baseFont), 2, 0);
        positionPanel.Controls.Add(CreateLabeledNumeric("Altura", _heightBox, baseFont), 3, 0);

        AddRow(layout, "Posicionamento", positionPanel);
        AddRow(layout, string.Empty, _topMostCheck);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var okButton = new Button
        {
            Text = "Salvar",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        okButton.Click += OnSaveRequested;

        var cancelButton = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        PopulateMonitors(template?.Window.Monitor);
        LoadTemplate(template);
        UpdateWindowFieldsState();
    }

    public AppConfig? Result { get; private set; }

    private static TextBox CreateTextBox(Font baseFont)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = baseFont,
        };
    }

    private static NumericUpDown CreateNumericBox(int minimum = 0, int maximum = 40000, int defaultValue = 0)
    {
        return new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            Value = defaultValue,
            DecimalPlaces = 0,
            Increment = 1,
        };
    }

    private static Control CreateLabeledNumeric(string caption, Control editor, Font font)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var label = new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = font,
            AutoSize = true,
        };

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(editor, 0, 1);
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (!string.IsNullOrEmpty(caption))
        {
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 6),
            };
            panel.Controls.Add(label, 0, row);
        }
        else
        {
            panel.Controls.Add(new Label { AutoSize = true }, 0, row);
        }

        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private void PopulateMonitors(MonitorKey? selected)
    {
        _monitorBox.Items.Clear();

        foreach (var monitor in _monitors)
        {
            var name = string.IsNullOrWhiteSpace(monitor.Name)
                ? $"Monitor {monitor.Key.DisplayIndex + 1}"
                : monitor.Name;

            var displayName = $"{name} ({monitor.Width}x{monitor.Height} - {monitor.Scale:P0})";
            _monitorBox.Items.Add(new MonitorOption(displayName, monitor));
        }

        if (_monitorBox.Items.Count == 0)
        {
            _monitorBox.Items.Add(new MonitorOption("Sem monitores disponíveis", new MonitorInfo()));
            _monitorBox.SelectedIndex = 0;
            _monitorBox.Enabled = false;
            return;
        }

        _monitorBox.Enabled = true;

        if (selected is not null)
        {
            for (var i = 0; i < _monitorBox.Items.Count; i++)
            {
                if (_monitorBox.Items[i] is MonitorOption option && MonitorKeysEqual(option.Monitor.Key, selected))
                {
                    _monitorBox.SelectedIndex = i;
                    return;
                }
            }
        }

        _monitorBox.SelectedIndex = 0;
    }

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
        _fullScreenCheck.Checked = template.Window.FullScreen;
        _topMostCheck.Checked = template.Window.AlwaysOnTop;

        if (!template.Window.FullScreen)
        {
            _xBox.Value = ClampNumeric(template.Window.X);
            _yBox.Value = ClampNumeric(template.Window.Y);
            _widthBox.Value = ClampNumeric(template.Window.Width, _widthBox.Minimum, _widthBox.Maximum, (int)_widthBox.Value);
            _heightBox.Value = ClampNumeric(template.Window.Height, _heightBox.Minimum, _heightBox.Maximum, (int)_heightBox.Value);
        }
    }

    private static decimal ClampNumeric(int? value, decimal min = 0, decimal max = 40000, int fallback = 0)
    {
        if (!value.HasValue)
        {
            return fallback;
        }

        var clamped = Math.Clamp(value.Value, (int)min, (int)max);
        return clamped;
    }

    private void OnBrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*",
            Title = "Selecionar executável",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathBox.Text = dialog.FileName;
        }
    }

    private void UpdateWindowFieldsState()
    {
        var enabled = !_fullScreenCheck.Checked;
        _xBox.Enabled = enabled;
        _yBox.Enabled = enabled;
        _widthBox.Enabled = enabled;
        _heightBox.Enabled = enabled;
    }

    private void OnSaveRequested(object? sender, EventArgs e)
    {
        if (!ValidateInputs())
        {
            DialogResult = DialogResult.None;
            return;
        }

        var monitor = ResolveSelectedMonitor();
        var window = BuildWindowConfig(monitor);

        Result = new AppConfig
        {
            Id = _nameBox.Text.Trim(),
            ExecutablePath = _pathBox.Text.Trim(),
            Arguments = string.IsNullOrWhiteSpace(_argsBox.Text) ? null : _argsBox.Text.Trim(),
            Window = window with { Title = _titleBox.Text?.Trim() ?? string.Empty },
        };
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Informe um nome para o aplicativo.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(_pathBox.Text))
        {
            MessageBox.Show(this, "Informe o caminho do executável.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _pathBox.Focus();
            return false;
        }

        if (!File.Exists(_pathBox.Text))
        {
            var answer = MessageBox.Show(
                this,
                "O executável informado não foi encontrado. Deseja continuar assim mesmo?",
                "Arquivo não encontrado",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                _pathBox.Focus();
                return false;
            }
        }

        return true;
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
        var selectedMonitor = WindowPlacementHelper.ResolveMonitor(null, _monitors, new WindowConfig { Monitor = monitor.Key });
        var fullScreen = _fullScreenCheck.Checked;

        if (fullScreen)
        {
            return new WindowConfig
            {
                Monitor = selectedMonitor.Key,
                FullScreen = true,
                AlwaysOnTop = _topMostCheck.Checked,
            };
        }

        return new WindowConfig
        {
            Monitor = selectedMonitor.Key,
            X = (int)_xBox.Value,
            Y = (int)_yBox.Value,
            Width = (int)_widthBox.Value,
            Height = (int)_heightBox.Value,
            FullScreen = false,
            AlwaysOnTop = _topMostCheck.Checked,
        };
    }

    private static bool MonitorKeysEqual(MonitorKey left, MonitorKey? right)
    {
        if (right is null)
        {
            return false;
        }

        return string.Equals(left.DeviceId, right.DeviceId, StringComparison.OrdinalIgnoreCase)
            && left.DisplayIndex == right.DisplayIndex
            && left.AdapterLuidHigh == right.AdapterLuidHigh
            && left.AdapterLuidLow == right.AdapterLuidLow
            && left.TargetId == right.TargetId;
    }

    private sealed record class MonitorOption(string DisplayName, MonitorInfo Monitor)
    {
        public override string ToString() => DisplayName;
    }
}
