#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed partial class SiteConfigTab : WinForms.UserControl
{
    private readonly BindingList<string> _hosts = new();
    private SiteConfig? _site;

    public SiteConfigTab()
    {
        InitializeComponent();

        _ = layoutPrincipal ?? throw new InvalidOperationException("Layout principal não foi carregado.");
        _ = txtUrl ?? throw new InvalidOperationException("Campo de URL não foi carregado.");
        _ = txtProfileName ?? throw new InvalidOperationException("Campo de ProfileName não foi carregado.");
        _ = txtSessionSelector ?? throw new InvalidOperationException("Campo de SessionSelector não foi carregado.");
        _ = lstAllowedHosts ?? throw new InvalidOperationException("Lista de hosts não foi carregada.");
        _ = txtHostEntrada ?? throw new InvalidOperationException("Campo de host não foi carregado.");
        _ = btnAdicionarHost ?? throw new InvalidOperationException("Botão Adicionar não foi carregado.");
        _ = btnRemoverHost ?? throw new InvalidOperationException("Botão Remover não foi carregado.");
        _ = btnValidarUrl ?? throw new InvalidOperationException("Botão Validar não foi carregado.");
        _ = btnTestarSessao ?? throw new InvalidOperationException("Botão Testar não foi carregado.");
        _ = errorProvider ?? throw new InvalidOperationException("ErrorProvider não foi carregado.");

        lstAllowedHosts.DataSource = _hosts;
        txtUrl.Validating += (_, e) => ValidateUrlInput(e);
        txtHostEntrada.Validating += (_, e) => ValidateHostInput(e);
    }

    public void Bind(SiteConfig? site)
    {
        _site = site;
        _hosts.Clear();

        if (site is null)
        {
            Enabled = false;
            txtUrl.Text = string.Empty;
            txtProfileName.Text = string.Empty;
            txtSessionSelector.Text = string.Empty;
            return;
        }

        Enabled = true;
        txtUrl.Text = site.Url;
        txtProfileName.Text = site.ProfileDirectory ?? string.Empty;
        txtSessionSelector.Text = string.Empty;

        foreach (var host in site.AllowedTabHosts ?? Enumerable.Empty<string>())
        {
            _hosts.Add(host);
        }
    }

    private void btnAdicionarHost_Click(object? sender, EventArgs e)
    {
        if (!ValidateHostInput())
        {
            return;
        }

        var input = txtHostEntrada.Text.Trim();
        if (!TryNormalizeHost(input, out var sanitized))
        {
            return;
        }

        if (!_hosts.Any(existing => string.Equals(existing, sanitized, StringComparison.OrdinalIgnoreCase)))
        {
            _hosts.Add(sanitized);
        }

        txtHostEntrada.Clear();
    }

    private void btnRemoverHost_Click(object? sender, EventArgs e)
    {
        if (lstAllowedHosts.SelectedItem is string host)
        {
            _hosts.Remove(host);
        }
    }

    private void btnValidarUrl_Click(object? sender, EventArgs e)
    {
        if (!Uri.TryCreate(txtUrl.Text.Trim(), UriKind.Absolute, out var uri))
        {
            WinForms.MessageBox.Show(this, "URL inválida.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        if (!TryNormalizeHost(uri.Host, out var sanitized))
        {
            WinForms.MessageBox.Show(this, "Host não pôde ser normalizado.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        WinForms.MessageBox.Show(this, $"URL válida para host '{sanitized}'.", "Validação", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
    }

    private void btnTestarSessao_Click(object? sender, EventArgs e)
    {
        if (_site is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um site antes de testar.", "Sessão", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        WinForms.MessageBox.Show(this, "Teste de sessão não está disponível nesta versão.", "Sessão", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
    }

    private void ValidateUrlInput(CancelEventArgs e)
    {
        var texto = txtUrl.Text.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            errorProvider.SetError(txtUrl, "Informe uma URL.");
            e.Cancel = true;
            return;
        }

        if (!Uri.TryCreate(texto, UriKind.Absolute, out _))
        {
            errorProvider.SetError(txtUrl, "URL inválida.");
            e.Cancel = true;
        }
        else
        {
            errorProvider.SetError(txtUrl, string.Empty);
        }
    }

    private void ValidateHostInput(CancelEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtHostEntrada.Text))
        {
            errorProvider.SetError(txtHostEntrada, string.Empty);
            return;
        }

        if (!TryNormalizeHost(txtHostEntrada.Text, out _))
        {
            errorProvider.SetError(txtHostEntrada, "Host inválido.");
            e.Cancel = true;
        }
        else
        {
            errorProvider.SetError(txtHostEntrada, string.Empty);
        }
    }

    private bool ValidateHostInput()
    {
        var args = new CancelEventArgs();
        ValidateHostInput(args);
        return !args.Cancel;
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
