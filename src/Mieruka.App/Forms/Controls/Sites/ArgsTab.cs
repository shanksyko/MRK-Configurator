#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed partial class ArgsTab : WinForms.UserControl
{
    private SiteConfig? _site;

    public ArgsTab()
    {
        InitializeComponent();
        DoubleBuffered = true;

        _ = layoutPrincipal ?? throw new InvalidOperationException("Layout principal não foi carregado.");
        _ = chkKiosk ?? throw new InvalidOperationException("CheckBox kiosk não foi carregado.");
        _ = chkAppMode ?? throw new InvalidOperationException("CheckBox app não foi carregado.");
        _ = chkIncognito ?? throw new InvalidOperationException("CheckBox incognito não foi carregado.");
        _ = txtProxy ?? throw new InvalidOperationException("Campo proxy não foi carregado.");
        _ = txtBypass ?? throw new InvalidOperationException("Campo bypass não foi carregado.");
        _ = nudTimeout ?? throw new InvalidOperationException("Campo timeout não foi carregado.");
        _ = nudPostLoginDelay ?? throw new InvalidOperationException("Campo delay não foi carregado.");
        _ = txtPreview ?? throw new InvalidOperationException("Prévia não foi carregada.");

        chkKiosk.CheckedChanged += (_, _) => UpdatePreview();
        chkAppMode.CheckedChanged += (_, _) => UpdatePreview();
        chkIncognito.CheckedChanged += (_, _) => UpdatePreview();
        txtProxy.TextChanged += (_, _) => UpdatePreview();
        txtBypass.TextChanged += (_, _) => UpdatePreview();
        nudTimeout.ValueChanged += (_, _) => UpdatePreview();
        nudPostLoginDelay.ValueChanged += (_, _) => UpdatePreview();
    }

    public void BindSite(SiteConfig? site)
    {
        _site = site;
        SuspendLayout();
        try
        {
            if (site is null)
            {
                Enabled = false;
                txtPreview.Clear();
                return;
            }

            Enabled = true;
            chkKiosk.Checked = site.KioskMode;
            chkAppMode.Checked = site.AppMode;
            chkIncognito.Checked = site.BrowserArguments?.Any(argument =>
                argument.Contains("--incognito", StringComparison.OrdinalIgnoreCase)) ?? false;

            // Extract proxy/bypass from existing browser arguments.
            txtProxy.Text = ExtractArgumentValue(site.BrowserArguments, "--proxy-server=");
            txtBypass.Text = ExtractArgumentValue(site.BrowserArguments, "--proxy-bypass-list=");

            nudTimeout.Value = Math.Clamp(site.Login?.TimeoutSeconds ?? 30, (int)nudTimeout.Minimum, (int)nudTimeout.Maximum);
            nudPostLoginDelay.Value = Math.Clamp(10, (int)nudPostLoginDelay.Minimum, (int)nudPostLoginDelay.Maximum);
            UpdatePreview();
        }
        finally
        {
            ResumeLayout(false);
            PerformLayout();
        }
    }

    /// <summary>
    /// Collects the current argument values from the UI controls.
    /// </summary>
    public (bool Kiosk, bool AppMode, bool Incognito, int Timeout, int PostLoginDelay, string? Proxy, string? ProxyBypass) CollectArgs()
    {
        return (
            chkKiosk.Checked,
            chkAppMode.Checked,
            chkIncognito.Checked,
            (int)nudTimeout.Value,
            (int)nudPostLoginDelay.Value,
            string.IsNullOrWhiteSpace(txtProxy.Text) ? null : txtProxy.Text.Trim(),
            string.IsNullOrWhiteSpace(txtBypass.Text) ? null : txtBypass.Text.Trim());
    }

    private static string ExtractArgumentValue(IReadOnlyList<string>? arguments, string prefix)
    {
        if (arguments is null)
        {
            return string.Empty;
        }

        foreach (var arg in arguments)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[prefix.Length..].Trim('"');
                return value;
            }
        }

        return string.Empty;
    }

    private void UpdatePreview()
    {
        if (_site is null)
        {
            txtPreview.Clear();
            return;
        }

        var args = new List<string>();
        if (chkKiosk.Checked)
        {
            args.Add("--kiosk");
        }
        if (chkAppMode.Checked)
        {
            var url = _site.Url;
            args.Add(string.IsNullOrWhiteSpace(url) ? "--app" : $"--app=\"{url}\"");
        }
        if (chkIncognito.Checked)
        {
            args.Add("--incognito");
        }
        if (!string.IsNullOrWhiteSpace(txtProxy.Text))
        {
            args.Add($"--proxy-server=\"{txtProxy.Text.Trim()}\"");
        }
        if (!string.IsNullOrWhiteSpace(txtBypass.Text))
        {
            args.Add($"--proxy-bypass-list=\"{txtBypass.Text.Trim()}\"");
        }

        var timeout = (int)nudTimeout.Value;
        var postDelay = (int)nudPostLoginDelay.Value;

        txtPreview.Text = string.Join(" ", args) + Environment.NewLine +
            $"Timeout={timeout}s, PostLoginDelay={postDelay}s";
    }
}
