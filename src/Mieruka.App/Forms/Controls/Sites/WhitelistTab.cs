#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed partial class WhitelistTab : WinForms.UserControl
{
    private readonly BindingList<string> _hosts = new();
    private SiteConfig? _site;

    public WhitelistTab()
    {
        InitializeComponent();

        _ = layoutPrincipal ?? throw new InvalidOperationException("Layout principal não inicializado.");
        _ = lstHosts ?? throw new InvalidOperationException("Lista de hosts não carregada.");
        _ = txtHostEntrada ?? throw new InvalidOperationException("Entrada de host não carregada.");
        _ = errorProvider ?? throw new InvalidOperationException("ErrorProvider não configurado.");

        lstHosts.DataSource = _hosts;
        txtHostEntrada.Validating += (_, e) => ValidateHostInput(e);
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

    private void btnAdicionar_Click(object? sender, EventArgs e)
    {
        if (!ValidateChildren())
        {
            return;
        }

        if (!TryNormalizeHost(txtHostEntrada.Text, out var sanitized))
        {
            return;
        }

        if (!_hosts.Any(existing => string.Equals(existing, sanitized, StringComparison.OrdinalIgnoreCase)))
        {
            _hosts.Add(sanitized);
        }

        txtHostEntrada.Clear();
    }

    private void btnRemover_Click(object? sender, EventArgs e)
    {
        if (lstHosts.SelectedItem is string host)
        {
            _hosts.Remove(host);
        }
    }

    private void btnNormalizar_Click(object? sender, EventArgs e)
    {
        if (lstHosts.SelectedItem is not string host)
        {
            return;
        }

        if (!TryNormalizeHost(host, out var normalized))
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

    private void ValidateHostInput(CancelEventArgs e)
    {
        var texto = txtHostEntrada.Text.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            errorProvider.SetError(txtHostEntrada, string.Empty);
            return;
        }

        if (!TryNormalizeHost(texto, out _))
        {
            errorProvider.SetError(txtHostEntrada, "Host inválido.");
            e.Cancel = true;
        }
        else
        {
            errorProvider.SetError(txtHostEntrada, string.Empty);
        }
    }

    private static bool TryNormalizeHost(string? input, out string sanitized)
    {
        sanitized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = InputSanitizer.SanitizeHost(input);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        sanitized = normalized;
        return true;
    }
}
