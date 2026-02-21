using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.App.Config;
using Mieruka.App.Services;
using Mieruka.Core.Layouts;
using Mieruka.Core.Models;
using System.Text;
using Serilog;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Ui;

internal sealed class SiteEditorDialog : WinForms.Form
{
    private static readonly ILogger Logger = Log.ForContext<SiteEditorDialog>();
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly IReadOnlyList<ZonePreset> _zonePresets;
    private readonly HashSet<string> _existingIds;
    private readonly string? _originalId;
    private readonly List<MonitorOption> _monitorOptions = new();
    private readonly List<ZoneOption> _zoneOptions = new();
    private readonly WinForms.TextBox _nameBox;
    private readonly WinForms.TextBox _titleBox;
    private readonly WinForms.TextBox _urlBox;
    private readonly WinForms.ComboBox _browserBox;
    private readonly WinForms.Label _browserStatusLabel;
    private readonly WinForms.TextBox _userDataBox;
    private readonly WinForms.TextBox _profileBox;
    private readonly WinForms.TextBox _argsBox;
    private readonly WinForms.TextBox _allowedHostsBox;
    private readonly WinForms.TextBox _commandPreviewBox;
    private readonly WinForms.CheckBox _appModeCheck;
    private readonly WinForms.CheckBox _kioskCheck;
    private readonly WinForms.CheckBox _reloadCheck;
    private readonly WinForms.NumericUpDown _reloadIntervalBox;
    private readonly WinForms.ComboBox _monitorBox;
    private readonly WinForms.ComboBox _zoneBox;
    private readonly WinForms.CheckBox _topMostCheck;

    private readonly WinForms.TextBox _loginUserBox;
    private readonly WinForms.TextBox _loginPassBox;
    private readonly WinForms.TextBox _loginUserSelectorBox;
    private readonly WinForms.TextBox _loginPassSelectorBox;
    private readonly WinForms.TextBox _loginSubmitSelectorBox;
    private readonly WinForms.TextBox _loginScriptBox;
    private readonly WinForms.NumericUpDown _loginTimeoutBox;

    private readonly WinForms.Button _testButton;

    private bool _suppressMonitorEvents;

    public SiteEditorDialog(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<ZonePreset> zonePresets,
        SiteConfig? template = null,
        string? selectedMonitorStableId = null,
        IEnumerable<string>? existingIds = null)
    {
        _monitors = monitors ?? Array.Empty<MonitorInfo>();
        _zonePresets = zonePresets ?? Array.Empty<ZonePreset>();
        _existingIds = new HashSet<string>(existingIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _originalId = template?.Id;

        AutoScaleMode = WinForms.AutoScaleMode.Dpi;
        AutoScaleDimensions = new Drawing.SizeF(96f, 96f);
        MinimumSize = new Drawing.Size(900, 600);
        StartPosition = WinForms.FormStartPosition.CenterParent;
        FormBorderStyle = WinForms.FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Text = template is null ? "Adicionar Site" : "Editar Site";

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
        _urlBox = CreateTextBox(baseFont);
        _userDataBox = CreateTextBox(baseFont);
        _userDataBox.TextChanged += (_, _) => UpdateBrowserPreview();
        _profileBox = CreateTextBox(baseFont);
        _profileBox.TextChanged += (_, _) => UpdateBrowserPreview();

        _browserBox = new WinForms.ComboBox
        {
            Dock = WinForms.DockStyle.Fill,
            DropDownStyle = WinForms.ComboBoxStyle.DropDownList,
            Font = baseFont,
        };
        foreach (var browser in Enum.GetValues<BrowserType>())
        {
            _browserBox.Items.Add(browser);
        }
        _browserBox.SelectedIndexChanged += (_, _) =>
        {
            UpdateBrowserDetectionStatus();
            UpdateBrowserPreview();
        };

        _browserStatusLabel = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
            AutoSize = true,
            ForeColor = Drawing.Color.Gray,
        };

        _argsBox = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 80,
        };
        _argsBox.TextChanged += (_, _) => UpdateBrowserPreview();

        _allowedHostsBox = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 80,
        };

        _commandPreviewBox = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = new Drawing.Font(FontFamily.GenericMonospace, 9f, FontStyle.Regular, GraphicsUnit.Point),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 60,
        };

        _appModeCheck = new WinForms.CheckBox { Text = "Modo aplicativo", Dock = WinForms.DockStyle.Fill, Font = baseFont };
        _appModeCheck.CheckedChanged += (_, _) => UpdateBrowserPreview();
        _kioskCheck = new WinForms.CheckBox { Text = "Modo quiosque", Dock = WinForms.DockStyle.Fill, Font = baseFont };
        _kioskCheck.CheckedChanged += (_, _) => UpdateBrowserPreview();
        _reloadCheck = new WinForms.CheckBox { Text = "Recarregar ao ativar", Dock = WinForms.DockStyle.Fill, Font = baseFont };
        _reloadCheck.CheckedChanged += (_, _) => UpdateReloadState();
        _reloadIntervalBox = new WinForms.NumericUpDown
        {
            Dock = WinForms.DockStyle.Left,
            Minimum = 5,
            Maximum = 3600,
            Value = 60,
            Enabled = false,
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

        _topMostCheck = new WinForms.CheckBox { Text = "Sempre no topo", Dock = WinForms.DockStyle.Fill, Font = baseFont };

        _loginUserBox = CreateTextBox(baseFont);
        _loginPassBox = CreateTextBox(baseFont);
        _loginPassBox.UseSystemPasswordChar = true;
        _loginUserSelectorBox = CreateTextBox(baseFont);
        _loginPassSelectorBox = CreateTextBox(baseFont);
        _loginSubmitSelectorBox = CreateTextBox(baseFont);
        _loginScriptBox = new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 80,
        };
        _loginTimeoutBox = new WinForms.NumericUpDown
        {
            Dock = WinForms.DockStyle.Left,
            Minimum = 1,
            Maximum = 300,
            Value = 15,
        };

        AddRow(layout, "Nome", _nameBox);
        AddRow(layout, "Título da janela", _titleBox);
        AddRow(layout, "URL", _urlBox);
        _urlBox.TextChanged += (_, _) => UpdateBrowserPreview();
        AddRow(layout, "Navegador", _browserBox);
        AddRow(layout, "", _browserStatusLabel);
        AddRow(layout, "User data dir", _userDataBox);
        AddRow(layout, "Profile dir", _profileBox);
        AddRow(layout, "Argumentos (um por linha)", _argsBox);
        AddRow(layout, "Linha de comando", _commandPreviewBox);
        AddRow(layout, "Hosts permitidos (um por linha)", _allowedHostsBox);
        AddRow(layout, string.Empty, _appModeCheck);
        AddRow(layout, string.Empty, _kioskCheck);

        var reloadPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        reloadPanel.Controls.Add(_reloadCheck);
        reloadPanel.Controls.Add(new WinForms.Label
        {
            Text = "Intervalo (s):",
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new WinForms.Padding(12, 6, 6, 0),
        });
        reloadPanel.Controls.Add(_reloadIntervalBox);
        AddRow(layout, string.Empty, reloadPanel);

        AddRow(layout, "Monitor", _monitorBox);
        AddRow(layout, "Zona", _zoneBox);
        AddRow(layout, string.Empty, _topMostCheck);

        var loginGroup = new WinForms.GroupBox
        {
            Text = "Login Automático",
            Dock = WinForms.DockStyle.Fill,
            Font = baseFont,
            Padding = new WinForms.Padding(10),
        };
        var loginLayout = new WinForms.TableLayoutPanel
        {
            Dock = WinForms.DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        loginLayout.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 30f));
        loginLayout.ColumnStyles.Add(new WinForms.ColumnStyle(SizeType.Percent, 70f));

        AddRow(loginLayout, "Usuário", _loginUserBox);
        AddRow(loginLayout, "Senha", _loginPassBox);
        AddRow(loginLayout, "Selector usuário", _loginUserSelectorBox);
        AddRow(loginLayout, "Selector senha", _loginPassSelectorBox);
        AddRow(loginLayout, "Selector submit", _loginSubmitSelectorBox);
        AddRow(loginLayout, "Script", _loginScriptBox);
        AddRow(loginLayout, "Timeout (s)", _loginTimeoutBox);

        loginGroup.Controls.Add(loginLayout);
        AddRow(layout, string.Empty, loginGroup);

        var buttonPanel = new WinForms.FlowLayoutPanel
        {
            Dock = WinForms.DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new WinForms.Padding(16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var okButton = new WinForms.Button { Text = "Salvar", AutoSize = true, DialogResult = WinForms.DialogResult.OK };
        okButton.Click += OnSaveRequested;
        var cancelButton = new WinForms.Button { Text = "Cancelar", AutoSize = true, DialogResult = WinForms.DialogResult.Cancel };
        _testButton = new WinForms.Button { Text = "Testar", AutoSize = true };
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
        UpdateReloadState();
        UpdateBrowserDetectionStatus();
        UpdateBrowserPreview();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<SiteConfig, Task<bool>>? TestHandler { get; set; }

    public SiteConfig? Result { get; private set; }

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

    private static WinForms.TextBox CreateTextBox(Drawing.Font font)
    {
        return new WinForms.TextBox
        {
            Dock = WinForms.DockStyle.Fill,
            Font = font,
        };
    }

    private static WinForms.NumericUpDown CreateNumericBox(int minimum = 0, int maximum = 40000, int defaultValue = 0)
    {
        return new WinForms.NumericUpDown
        {
            Dock = WinForms.DockStyle.Fill,
            Minimum = minimum,
            Maximum = maximum,
            Value = defaultValue,
            DecimalPlaces = 0,
            Increment = 1,
        };
    }

    private static void AddRow(WinForms.TableLayoutPanel panel, string caption, WinForms.Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new WinForms.RowStyle(SizeType.AutoSize));
        if (!string.IsNullOrEmpty(caption))
        {
            panel.Controls.Add(new WinForms.Label
            {
                Text = caption,
                Dock = WinForms.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new WinForms.Padding(0, 0, 6, 6),
            }, 0, row);
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
        _topMostCheck.Checked = template.Window.AlwaysOnTop;

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

        UpdateBrowserPreview();
    }

    private MonitorInfo ResolveSelectedMonitor()
    {
        if (_monitorBox.SelectedItem is MonitorOption option)
        {
            return option.Monitor;
        }

        return _monitors.FirstOrDefault() ?? new MonitorInfo();
    }

    private void UpdateBrowserPreview()
    {
        if (_commandPreviewBox is null)
        {
            return;
        }

        var browser = _browserBox.SelectedItem is BrowserType selected
            ? selected
            : BrowserType.Chrome;
        var enginePath = ResolveBrowserExecutable(browser);

        var arguments = new List<string>();

        void AddArgument(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            arguments.Add(value);
        }

        var userData = _userDataBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(userData))
        {
            AddArgument(FormatArgument("--user-data-dir", userData));
        }

        var profile = _profileBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(profile))
        {
            AddArgument(FormatArgument("--profile-directory", profile));
        }

        if (_kioskCheck.Checked)
        {
            AddArgument("--kiosk");
        }

        var url = _urlBox.Text.Trim();
        if (_appModeCheck.Checked && !string.IsNullOrWhiteSpace(url))
        {
            AddArgument(FormatArgument("--app", url));
        }

        foreach (var line in _argsBox.Lines ?? Array.Empty<string>())
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                AddArgument(trimmed);
            }
        }

        if (!_appModeCheck.Checked && !string.IsNullOrWhiteSpace(url))
        {
            AddArgument(url);
        }

        var preview = new StringBuilder();
        preview.Append('"').Append(enginePath).Append('"');

        if (arguments.Count > 0)
        {
            preview.Append(' ').Append(string.Join(' ', arguments));
        }

        _commandPreviewBox.Text = preview.ToString().Trim();
    }

    private static string FormatArgument(string name, string value)
    {
        var sanitized = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"{name}=\"{sanitized}\"";
    }

    private static string ResolveBrowserExecutable(BrowserType browser)
    {
        if (OperatingSystem.IsWindows())
        {
            return browser switch
            {
                BrowserType.Chrome => "chrome.exe",
                BrowserType.Edge => "msedge.exe",
                BrowserType.Firefox => "firefox.exe",
                BrowserType.Brave => "brave.exe",
                _ => browser.ToString(),
            };
        }

        return browser switch
        {
            BrowserType.Chrome => "google-chrome",
            BrowserType.Edge => "microsoft-edge",
            BrowserType.Firefox => "firefox",
            BrowserType.Brave => "brave-browser",
            _ => browser.ToString().ToLowerInvariant(),
        };
    }

    private void UpdateBrowserDetectionStatus()
    {
        if (_browserStatusLabel is null || _browserBox.SelectedItem is not BrowserType browser)
        {
            return;
        }

        try
        {
            var installations = BrowserRegistry.Detect();
            var match = installations.FirstOrDefault(b => b.Engine == browser);
            if (match is not null && match.IsDetected)
            {
                _browserStatusLabel.Text = $"✓ {match.DisplayName} detectado: {match.ExecutablePath}";
                _browserStatusLabel.ForeColor = Drawing.Color.Green;
            }
            else
            {
                _browserStatusLabel.Text = $"⚠ {browser} não encontrado no sistema";
                _browserStatusLabel.ForeColor = Drawing.Color.OrangeRed;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Não foi possível verificar o navegador selecionado.");
            _browserStatusLabel.Text = "Não foi possível verificar o navegador";
            _browserStatusLabel.ForeColor = Drawing.Color.Gray;
        }
    }

    private void UpdateReloadState()
    {
        _reloadIntervalBox.Enabled = _reloadCheck.Checked;
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

    private SiteConfig CreateConfigFromInputs()
    {
        var monitor = ResolveSelectedMonitor();
        var window = BuildWindowConfig(monitor);
        var zoneIdentifier = GetSelectedZoneIdentifier();

        return new SiteConfig
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
            TargetMonitorStableId = WindowPlacementHelper.ResolveStableId(monitor),
            TargetZonePresetId = zoneIdentifier,
        };
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

    private static List<string> ParseLines(string text)
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

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            WinForms.MessageBox.Show(this, "Informe um nome para o site.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _nameBox.Focus();
            return false;
        }

        var id = _nameBox.Text.Trim();
        if (!string.Equals(id, _originalId, StringComparison.OrdinalIgnoreCase) && _existingIds.Contains(id))
        {
            WinForms.MessageBox.Show(this, $"Já existe um site com o nome '{id}'.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _nameBox.Focus();
            return false;
        }

        if (_browserBox.SelectedItem is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um navegador para o site.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _browserBox.Focus();
            return false;
        }

        if (!_appModeCheck.Checked && string.IsNullOrWhiteSpace(_urlBox.Text))
        {
            WinForms.MessageBox.Show(this, "Informe a URL do site.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _urlBox.Focus();
            return false;
        }

        if (_appModeCheck.Checked && string.IsNullOrWhiteSpace(_urlBox.Text))
        {
            WinForms.MessageBox.Show(this, "Modo aplicativo requer uma URL.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _urlBox.Focus();
            return false;
        }

        var url = _urlBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri))
            {
                WinForms.MessageBox.Show(this, $"A URL '{url}' não é válida. Use formato http:// ou https://.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                _urlBox.Focus();
                return false;
            }

            if (parsedUri.Scheme is not ("http" or "https"))
            {
                WinForms.MessageBox.Show(this, "Apenas URLs http:// e https:// são suportadas.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                _urlBox.Focus();
                return false;
            }
        }

        if (_zoneBox.Enabled && _zoneBox.SelectedItem is not ZoneOption)
        {
            WinForms.MessageBox.Show(this, "Selecione uma zona para posicionar o site.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            _zoneBox.Focus();
            return false;
        }

        return true;
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
