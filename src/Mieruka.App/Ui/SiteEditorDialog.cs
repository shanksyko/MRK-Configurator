using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Services;
using Mieruka.Core.Models;

namespace Mieruka.App.Ui;

internal sealed class SiteEditorDialog : Form
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly TextBox _nameBox;
    private readonly TextBox _titleBox;
    private readonly TextBox _urlBox;
    private readonly ComboBox _browserBox;
    private readonly TextBox _userDataBox;
    private readonly TextBox _profileBox;
    private readonly TextBox _argsBox;
    private readonly TextBox _allowedHostsBox;
    private readonly CheckBox _appModeCheck;
    private readonly CheckBox _kioskCheck;
    private readonly CheckBox _reloadCheck;
    private readonly NumericUpDown _reloadIntervalBox;
    private readonly ComboBox _monitorBox;
    private readonly CheckBox _fullScreenCheck;
    private readonly NumericUpDown _xBox;
    private readonly NumericUpDown _yBox;
    private readonly NumericUpDown _widthBox;
    private readonly NumericUpDown _heightBox;
    private readonly CheckBox _topMostCheck;

    private readonly TextBox _loginUserBox;
    private readonly TextBox _loginPassBox;
    private readonly TextBox _loginUserSelectorBox;
    private readonly TextBox _loginPassSelectorBox;
    private readonly TextBox _loginSubmitSelectorBox;
    private readonly TextBox _loginScriptBox;
    private readonly NumericUpDown _loginTimeoutBox;

    public SiteEditorDialog(IReadOnlyList<MonitorInfo> monitors, SiteConfig? template = null)
    {
        _monitors = monitors ?? Array.Empty<MonitorInfo>();

        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Text = template is null ? "Adicionar Site" : "Editar Site";

        var baseFont = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        _nameBox = CreateTextBox(baseFont);
        _titleBox = CreateTextBox(baseFont);
        _urlBox = CreateTextBox(baseFont);
        _userDataBox = CreateTextBox(baseFont);
        _profileBox = CreateTextBox(baseFont);

        _browserBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = baseFont,
        };
        foreach (var browser in Enum.GetValues<BrowserType>())
        {
            _browserBox.Items.Add(browser);
        }

        _argsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 60,
        };

        _allowedHostsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 60,
        };

        _appModeCheck = new CheckBox { Text = "Modo aplicativo", Dock = DockStyle.Fill, Font = baseFont };
        _kioskCheck = new CheckBox { Text = "Modo quiosque", Dock = DockStyle.Fill, Font = baseFont };
        _reloadCheck = new CheckBox { Text = "Recarregar ao ativar", Dock = DockStyle.Fill, Font = baseFont };
        _reloadCheck.CheckedChanged += (_, _) => UpdateReloadState();
        _reloadIntervalBox = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Minimum = 5,
            Maximum = 3600,
            Value = 60,
            Enabled = false,
        };

        _monitorBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = baseFont,
        };

        _fullScreenCheck = new CheckBox { Text = "Tela cheia", Dock = DockStyle.Fill, Font = baseFont, Checked = true };
        _fullScreenCheck.CheckedChanged += (_, _) => UpdateWindowState();

        _xBox = CreateNumericBox();
        _yBox = CreateNumericBox();
        _widthBox = CreateNumericBox(100, defaultValue: 1024);
        _heightBox = CreateNumericBox(100, defaultValue: 768);
        _topMostCheck = new CheckBox { Text = "Sempre no topo", Dock = DockStyle.Fill, Font = baseFont };

        _loginUserBox = CreateTextBox(baseFont);
        _loginPassBox = CreateTextBox(baseFont);
        _loginPassBox.UseSystemPasswordChar = true;
        _loginUserSelectorBox = CreateTextBox(baseFont);
        _loginPassSelectorBox = CreateTextBox(baseFont);
        _loginSubmitSelectorBox = CreateTextBox(baseFont);
        _loginScriptBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 80,
        };
        _loginTimeoutBox = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Minimum = 1,
            Maximum = 300,
            Value = 15,
        };

        AddRow(layout, "Nome", _nameBox);
        AddRow(layout, "Título da janela", _titleBox);
        AddRow(layout, "URL", _urlBox);
        AddRow(layout, "Navegador", _browserBox);
        AddRow(layout, "User data dir", _userDataBox);
        AddRow(layout, "Profile dir", _profileBox);
        AddRow(layout, "Argumentos (um por linha)", _argsBox);
        AddRow(layout, "Hosts permitidos (um por linha)", _allowedHostsBox);
        AddRow(layout, string.Empty, _appModeCheck);
        AddRow(layout, string.Empty, _kioskCheck);

        var reloadPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        reloadPanel.Controls.Add(_reloadCheck);
        reloadPanel.Controls.Add(new Label
        {
            Text = "Intervalo (s):",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(12, 6, 6, 0),
        });
        reloadPanel.Controls.Add(_reloadIntervalBox);
        AddRow(layout, string.Empty, reloadPanel);

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

        var loginGroup = new GroupBox
        {
            Text = "Login Automático",
            Dock = DockStyle.Fill,
            Font = baseFont,
            Padding = new Padding(10),
        };
        var loginLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        loginLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        loginLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        AddRow(loginLayout, "Usuário", _loginUserBox);
        AddRow(loginLayout, "Senha", _loginPassBox);
        AddRow(loginLayout, "Selector usuário", _loginUserSelectorBox);
        AddRow(loginLayout, "Selector senha", _loginPassSelectorBox);
        AddRow(loginLayout, "Selector submit", _loginSubmitSelectorBox);
        AddRow(loginLayout, "Script", _loginScriptBox);
        AddRow(loginLayout, "Timeout (s)", _loginTimeoutBox);

        loginGroup.Controls.Add(loginLayout);
        AddRow(layout, string.Empty, loginGroup);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        var okButton = new Button { Text = "Salvar", AutoSize = true, DialogResult = DialogResult.OK };
        okButton.Click += OnSaveRequested;
        var cancelButton = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttonPanel.Controls.Add(okButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        PopulateMonitors(template?.Window.Monitor);
        LoadTemplate(template);
        UpdateReloadState();
        UpdateWindowState();
    }

    public SiteConfig? Result { get; private set; }

    private static TextBox CreateTextBox(Font font)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Font = font,
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
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Font = font, AutoSize = true }, 0, 0);
        panel.Controls.Add(editor, 0, 1);
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (!string.IsNullOrEmpty(caption))
        {
            panel.Controls.Add(new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 6),
            }, 0, row);
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

    private void LoadTemplate(SiteConfig? template)
    {
        if (template is null)
        {
            return;
        }

        _nameBox.Text = template.Id;
        _titleBox.Text = template.Window.Title;
        _urlBox.Text = template.Url;
        _browserBox.SelectedItem = template.Browser;
        _userDataBox.Text = template.UserDataDirectory ?? string.Empty;
        _profileBox.Text = template.ProfileDirectory ?? string.Empty;
        _argsBox.Text = string.Join(Environment.NewLine, template.BrowserArguments ?? Array.Empty<string>());
        _allowedHostsBox.Text = string.Join(Environment.NewLine, template.AllowedTabHosts ?? Array.Empty<string>());
        _appModeCheck.Checked = template.AppMode;
        _kioskCheck.Checked = template.KioskMode;
        _reloadCheck.Checked = template.ReloadOnActivate;
        if (template.ReloadIntervalSeconds.HasValue)
        {
            _reloadIntervalBox.Value = Math.Clamp(template.ReloadIntervalSeconds.Value, (int)_reloadIntervalBox.Minimum, (int)_reloadIntervalBox.Maximum);
        }
        _fullScreenCheck.Checked = template.Window.FullScreen;
        _topMostCheck.Checked = template.Window.AlwaysOnTop;
        if (!template.Window.FullScreen)
        {
            _xBox.Value = Clamp(template.Window.X, _xBox);
            _yBox.Value = Clamp(template.Window.Y, _yBox);
            _widthBox.Value = Clamp(template.Window.Width, _widthBox);
            _heightBox.Value = Clamp(template.Window.Height, _heightBox);
        }

        if (template.Login is not null)
        {
            _loginUserBox.Text = template.Login.Username;
            _loginPassBox.Text = template.Login.Password;
            _loginUserSelectorBox.Text = template.Login.UserSelector ?? string.Empty;
            _loginPassSelectorBox.Text = template.Login.PassSelector ?? string.Empty;
            _loginSubmitSelectorBox.Text = template.Login.SubmitSelector ?? string.Empty;
            _loginScriptBox.Text = template.Login.Script ?? string.Empty;
            _loginTimeoutBox.Value = Math.Clamp(template.Login.TimeoutSeconds, (int)_loginTimeoutBox.Minimum, (int)_loginTimeoutBox.Maximum);
        }
    }

    private static decimal Clamp(int? value, NumericUpDown target)
    {
        if (!value.HasValue)
        {
            return target.Value;
        }

        return Math.Clamp(value.Value, (int)target.Minimum, (int)target.Maximum);
    }

    private void UpdateReloadState()
    {
        _reloadIntervalBox.Enabled = _reloadCheck.Checked;
    }

    private void UpdateWindowState()
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

        Result = new SiteConfig
        {
            Id = _nameBox.Text.Trim(),
            Url = _urlBox.Text.Trim(),
            Browser = _browserBox.SelectedItem is BrowserType browser ? browser : BrowserType.Chrome,
            UserDataDirectory = string.IsNullOrWhiteSpace(_userDataBox.Text) ? null : _userDataBox.Text.Trim(),
            ProfileDirectory = string.IsNullOrWhiteSpace(_profileBox.Text) ? null : _profileBox.Text.Trim(),
            BrowserArguments = ParseLines(_argsBox.Text),
            AllowedTabHosts = ParseLines(_allowedHostsBox.Text),
            AppMode = _appModeCheck.Checked,
            KioskMode = _kioskCheck.Checked,
            ReloadOnActivate = _reloadCheck.Checked,
            ReloadIntervalSeconds = _reloadCheck.Checked ? (int?)_reloadIntervalBox.Value : null,
            Window = window with { Title = _titleBox.Text?.Trim() ?? string.Empty },
            Login = BuildLoginProfile(),
        };
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Informe um nome para o site.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameBox.Focus();
            return false;
        }

        if (_browserBox.SelectedItem is null)
        {
            MessageBox.Show(this, "Selecione um navegador para o site.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _browserBox.Focus();
            return false;
        }

        if (!_appModeCheck.Checked && string.IsNullOrWhiteSpace(_urlBox.Text))
        {
            MessageBox.Show(this, "Informe a URL do site.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _urlBox.Focus();
            return false;
        }

        if (_appModeCheck.Checked && string.IsNullOrWhiteSpace(_urlBox.Text))
        {
            MessageBox.Show(this, "Modo aplicativo requer uma URL.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _urlBox.Focus();
            return false;
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
        if (_fullScreenCheck.Checked)
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

    private LoginProfile? BuildLoginProfile()
    {
        var hasContent = !string.IsNullOrWhiteSpace(_loginUserBox.Text)
            || !string.IsNullOrWhiteSpace(_loginPassBox.Text)
            || !string.IsNullOrWhiteSpace(_loginUserSelectorBox.Text)
            || !string.IsNullOrWhiteSpace(_loginPassSelectorBox.Text)
            || !string.IsNullOrWhiteSpace(_loginSubmitSelectorBox.Text)
            || !string.IsNullOrWhiteSpace(_loginScriptBox.Text);

        if (!hasContent)
        {
            return null;
        }

        return new LoginProfile
        {
            Username = _loginUserBox.Text ?? string.Empty,
            Password = _loginPassBox.Text ?? string.Empty,
            UserSelector = string.IsNullOrWhiteSpace(_loginUserSelectorBox.Text) ? null : _loginUserSelectorBox.Text.Trim(),
            PassSelector = string.IsNullOrWhiteSpace(_loginPassSelectorBox.Text) ? null : _loginPassSelectorBox.Text.Trim(),
            SubmitSelector = string.IsNullOrWhiteSpace(_loginSubmitSelectorBox.Text) ? null : _loginSubmitSelectorBox.Text.Trim(),
            Script = string.IsNullOrWhiteSpace(_loginScriptBox.Text) ? null : _loginScriptBox.Text.Trim(),
            TimeoutSeconds = (int)_loginTimeoutBox.Value,
        };
    }

    private static IList<string> ParseLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        return text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
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
