using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed class SiteConfigTab : UserControl
{
    private readonly TextBox _urlBox;
    private readonly TextBox _profileBox;
    private readonly TextBox _sessionSelectorBox;
    private readonly TextBox _hostInput;
    private readonly BindingList<string> _allowedHosts = new();
    private readonly ListBox _allowedHostsList;
    private SiteConfig? _site;

    public SiteConfigTab()
    {
        LayoutHelpers.ApplyStandardLayout(this);

        var layout = LayoutHelpers.CreateStandardTableLayout();
        layout.RowCount = 4;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        header.Controls.Add(new Label { Text = "URL", AutoSize = true }, 0, 0);
        _urlBox = new TextBox { Dock = DockStyle.Fill };
        header.Controls.Add(_urlBox, 1, 0);

        header.Controls.Add(new Label { Text = "ProfileName", AutoSize = true }, 0, 1);
        _profileBox = new TextBox { Dock = DockStyle.Fill };
        header.Controls.Add(_profileBox, 1, 1);

        header.Controls.Add(new Label { Text = "SessionSelector", AutoSize = true }, 0, 2);
        _sessionSelectorBox = new TextBox { Dock = DockStyle.Fill };
        header.Controls.Add(_sessionSelectorBox, 1, 2);

        layout.Controls.Add(header, 0, 0);

        var hostsLabel = new Label
        {
            Text = "AllowedTabHosts",
            AutoSize = true,
        };
        layout.Controls.Add(hostsLabel, 0, 1);

        _allowedHostsList = new ListBox
        {
            Dock = DockStyle.Fill,
            DataSource = _allowedHosts,
        };
        layout.Controls.Add(_allowedHostsList, 0, 2);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };

        _hostInput = new TextBox { Width = 200 };
        commands.Controls.Add(_hostInput);

        var addButton = new Button { Text = "Adicionar", AutoSize = true };
        addButton.Click += (_, _) => AddHost();
        commands.Controls.Add(addButton);

        var removeButton = new Button { Text = "Remover", AutoSize = true };
        removeButton.Click += (_, _) => RemoveSelectedHost();
        commands.Controls.Add(removeButton);

        var normalizeButton = new Button { Text = "Normalizar", AutoSize = true };
        normalizeButton.Click += (_, _) => NormalizeSelectedHost();
        commands.Controls.Add(normalizeButton);

        var validateButton = new Button { Text = "Validar URL", AutoSize = true };
        validateButton.Click += (_, _) => ValidateUrl();
        commands.Controls.Add(validateButton);

        var testSessionButton = new Button { Text = "Testar Sessão", AutoSize = true };
        testSessionButton.Click += (_, _) => MessageBox.Show(
            this,
            "Teste de sessão não implementado.",
            "Sessão",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        commands.Controls.Add(testSessionButton);

        layout.Controls.Add(commands, 0, 3);

        Controls.Add(layout);
    }

    public void Bind(SiteConfig? site)
    {
        _site = site;
        _allowedHosts.Clear();

        if (site is null)
        {
            _urlBox.Text = string.Empty;
            _profileBox.Text = string.Empty;
            _sessionSelectorBox.Text = string.Empty;
            Enabled = false;
            return;
        }

        Enabled = true;
        _urlBox.Text = site.Url;
        _profileBox.Text = site.ProfileDirectory ?? string.Empty;
        _sessionSelectorBox.Text = string.Empty;

        foreach (var host in site.AllowedTabHosts ?? Enumerable.Empty<string>())
        {
            _allowedHosts.Add(host);
        }
    }

    private void AddHost()
    {
        var input = _hostInput.Text?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            return;
        }

        var sanitized = InputSanitizer.SanitizeHost(input);
        if (string.IsNullOrEmpty(sanitized))
        {
            MessageBox.Show(this, "Host inválido.", "AllowedTabHosts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_allowedHosts.Any(existing => string.Equals(existing, sanitized, StringComparison.OrdinalIgnoreCase)))
        {
            _allowedHosts.Add(sanitized);
        }

        _hostInput.Clear();
    }

    private void RemoveSelectedHost()
    {
        if (_allowedHostsList.SelectedItem is string host)
        {
            _allowedHosts.Remove(host);
        }
    }

    private void NormalizeSelectedHost()
    {
        if (_allowedHostsList.SelectedItem is not string host)
        {
            return;
        }

        var normalized = InputSanitizer.SanitizeHost(host);
        if (string.IsNullOrEmpty(normalized))
        {
            MessageBox.Show(this, "Host inválido.", "AllowedTabHosts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var index = _allowedHosts.IndexOf(host);
        if (index >= 0)
        {
            _allowedHosts[index] = normalized;
        }
    }

    private void ValidateUrl()
    {
        var text = _urlBox.Text?.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            var allowlist = new UrlAllowlist();
            allowlist.Add(uri.Host);
            var message = allowlist.IsAllowed(uri) ? "URL válida." : "URL rejeitada.";
            MessageBox.Show(this, message, "Validação", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, "URL inválida.", "Validação", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
