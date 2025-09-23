using System;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed class ArgsTab : UserControl
{
    private readonly CheckBox _kioskCheck;
    private readonly CheckBox _appModeCheck;
    private readonly CheckBox _incognitoCheck;
    private readonly TextBox _proxyBox;
    private readonly TextBox _bypassBox;
    private readonly NumericUpDown _timeoutUpDown;
    private readonly NumericUpDown _stabilizationUpDown;
    private readonly TextBox _previewBox;
    private SiteConfig? _site;

    public ArgsTab()
    {
        LayoutHelpers.ApplyStandardLayout(this);

        var layout = LayoutHelpers.CreateStandardTableLayout();
        layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var switches = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        _kioskCheck = new CheckBox { Text = "kiosk", AutoSize = true };
        _appModeCheck = new CheckBox { Text = "app", AutoSize = true };
        _incognitoCheck = new CheckBox { Text = "incognito", AutoSize = true };
        switches.Controls.Add(_kioskCheck);
        switches.Controls.Add(_appModeCheck);
        switches.Controls.Add(_incognitoCheck);
        layout.Controls.Add(switches, 0, 0);

        var proxyPanel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        proxyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        proxyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        proxyPanel.Controls.Add(new Label { Text = "proxy", AutoSize = true }, 0, 0);
        _proxyBox = new TextBox { Dock = DockStyle.Fill };
        proxyPanel.Controls.Add(_proxyBox, 1, 0);

        proxyPanel.Controls.Add(new Label { Text = "bypass", AutoSize = true }, 0, 1);
        _bypassBox = new TextBox { Dock = DockStyle.Fill };
        proxyPanel.Controls.Add(_bypassBox, 1, 1);
        layout.Controls.Add(proxyPanel, 0, 1);

        var timings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        timings.Controls.Add(new Label { Text = "Timeout (s)", AutoSize = true });
        _timeoutUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 600,
            Value = 30,
            Width = 80,
        };
        timings.Controls.Add(_timeoutUpDown);

        timings.Controls.Add(new Label { Text = "PostLogin Stabilization (s)", AutoSize = true });
        _stabilizationUpDown = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 600,
            Value = 10,
            Width = 80,
        };
        timings.Controls.Add(_stabilizationUpDown);
        layout.Controls.Add(timings, 0, 2);

        _previewBox = new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            Height = 120,
        };
        layout.Controls.Add(_previewBox, 0, 3);

        Controls.Add(layout);

        _kioskCheck.CheckedChanged += (_, _) => UpdatePreview();
        _appModeCheck.CheckedChanged += (_, _) => UpdatePreview();
        _incognitoCheck.CheckedChanged += (_, _) => UpdatePreview();
        _proxyBox.TextChanged += (_, _) => UpdatePreview();
        _bypassBox.TextChanged += (_, _) => UpdatePreview();
        _timeoutUpDown.ValueChanged += (_, _) => UpdatePreview();
        _stabilizationUpDown.ValueChanged += (_, _) => UpdatePreview();
    }

    public void BindSite(SiteConfig? site)
    {
        _site = site;
        if (site is null)
        {
            Enabled = false;
            _previewBox.Clear();
            return;
        }

        Enabled = true;
        _kioskCheck.Checked = site.KioskMode;
        _appModeCheck.Checked = site.AppMode;
        _incognitoCheck.Checked = site.BrowserArguments.Any(argument =>
            string.Equals(argument, "--incognito", StringComparison.OrdinalIgnoreCase));
        _proxyBox.Text = string.Empty;
        _bypassBox.Text = string.Empty;
        _timeoutUpDown.Value = site.Login?.TimeoutSeconds ?? 30;
        _stabilizationUpDown.Value = 10;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_site is null)
        {
            _previewBox.Clear();
            return;
        }

        var args = new System.Collections.Generic.List<string>();
        if (_kioskCheck.Checked)
        {
            args.Add("--kiosk");
        }
        if (_appModeCheck.Checked)
        {
            args.Add("--app-mode");
        }
        if (_incognitoCheck.Checked)
        {
            args.Add("--incognito");
        }
        if (!string.IsNullOrWhiteSpace(_proxyBox.Text))
        {
            args.Add($"--proxy-server={_proxyBox.Text.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(_bypassBox.Text))
        {
            args.Add($"--proxy-bypass-list={_bypassBox.Text.Trim()}");
        }

        var timeout = (int)_timeoutUpDown.Value;
        var stabilization = (int)_stabilizationUpDown.Value;

        _previewBox.Text = string.Join(" ", args) + Environment.NewLine +
            $"Timeout={timeout}s, Stabilization={stabilization}s";
    }
}
