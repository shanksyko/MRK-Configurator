using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed class WhitelistTab : UserControl
{
    private readonly BindingList<string> _hosts = new();
    private readonly ListBox _hostsList;
    private readonly TextBox _hostInput;
    private readonly ErrorProvider _errorProvider;
    private SiteConfig? _site;

    public WhitelistTab()
    {
        LayoutHelpers.ApplyStandardLayout(this);

        _errorProvider = new ErrorProvider
        {
            BlinkStyle = ErrorBlinkStyle.NeverBlink,
            ContainerControl = this,
        };

        var layout = LayoutHelpers.CreateStandardTableLayout();
        layout.RowCount = 2;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _hostsList = new ListBox
        {
            Dock = DockStyle.Fill,
            DataSource = _hosts,
        };
        layout.Controls.Add(_hostsList, 0, 0);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
        };

        _hostInput = new TextBox { Width = 220 };
        _hostInput.Validating += (_, e) => ValidateInput(e);
        footer.Controls.Add(_hostInput);

        var addButton = new Button { Text = "Adicionar", AutoSize = true };
        addButton.Click += (_, _) => AddHost();
        footer.Controls.Add(addButton);

        var removeButton = new Button { Text = "Remover", AutoSize = true };
        removeButton.Click += (_, _) => RemoveSelected();
        footer.Controls.Add(removeButton);

        var normalizeButton = new Button { Text = "Normalizar", AutoSize = true };
        normalizeButton.Click += (_, _) => NormalizeSelected();
        footer.Controls.Add(normalizeButton);

        layout.Controls.Add(footer, 0, 1);

        Controls.Add(layout);
    }

    public void BindSite(SiteConfig? site)
    {
        _site = site;
        _hosts.Clear();
        Enabled = site is not null;

        if (site is null)
        {
            return;
        }

        foreach (var host in site.AllowedTabHosts ?? Enumerable.Empty<string>())
        {
            _hosts.Add(host);
        }
    }

    private void ValidateInput(CancelEventArgs e)
    {
        var value = _hostInput.Text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            _errorProvider.SetError(_hostInput, string.Empty);
            return;
        }

        var normalized = InputSanitizer.SanitizeHost(value);
        if (string.IsNullOrEmpty(normalized))
        {
            _errorProvider.SetError(_hostInput, "Host inválido");
            e.Cancel = true;
        }
        else
        {
            _errorProvider.SetError(_hostInput, string.Empty);
        }
    }

    private void AddHost()
    {
        if (!_hostInput.ValidateChildren())
        {
            return;
        }

        var sanitized = InputSanitizer.SanitizeHost(_hostInput.Text);
        if (string.IsNullOrEmpty(sanitized))
        {
            return;
        }

        if (!_hosts.Any(existing => string.Equals(existing, sanitized, StringComparison.OrdinalIgnoreCase)))
        {
            _hosts.Add(sanitized);
        }

        _hostInput.Clear();
    }

    private void RemoveSelected()
    {
        if (_hostsList.SelectedItem is string host)
        {
            _hosts.Remove(host);
        }
    }

    private void NormalizeSelected()
    {
        if (_hostsList.SelectedItem is not string host)
        {
            return;
        }

        var normalized = InputSanitizer.SanitizeHost(host);
        if (string.IsNullOrEmpty(normalized))
        {
            _hosts.Remove(host);
            return;
        }

        var index = _hosts.IndexOf(host);
        if (index >= 0)
        {
            _hosts[index] = normalized;
        }
    }
}
