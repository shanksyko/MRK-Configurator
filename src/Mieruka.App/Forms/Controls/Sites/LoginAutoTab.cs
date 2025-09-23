#nullable enable
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Automation.Login;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed partial class LoginAutoTab : UserControl
{
    private readonly LoginOrchestrator _orchestrator = new();
    private readonly BindingList<string> _ssoHints = new();
    private SiteConfig? _site;

    public LoginAutoTab()
    {
        InitializeComponent();

        _ = layoutPrincipal ?? throw new InvalidOperationException("Layout principal não inicializado.");
        _ = txtUserSelector ?? throw new InvalidOperationException("Campo de usuário não carregado.");
        _ = txtPasswordSelector ?? throw new InvalidOperationException("Campo de senha não carregado.");
        _ = txtSubmitSelector ?? throw new InvalidOperationException("Campo de submit não carregado.");
        _ = txtPostSubmitSelector ?? throw new InvalidOperationException("Campo pós-submit não carregado.");
        _ = txtExtraWaitSelectors ?? throw new InvalidOperationException("Campo de waits não carregado.");
        _ = lstSsoHints ?? throw new InvalidOperationException("Lista de SSO não carregada.");
        _ = txtSsoHintEntrada ?? throw new InvalidOperationException("Entrada de SSO não carregada.");
        _ = cmbMfaTipo ?? throw new InvalidOperationException("Combo MFA não carregado.");
        _ = txtTotpSecretKeyRef ?? throw new InvalidOperationException("Campo de TOTP não carregado.");
        _ = btnDetectarCampos ?? throw new InvalidOperationException("Botão Detectar não carregado.");
        _ = btnTestarLogin ?? throw new InvalidOperationException("Botão Testar não carregado.");
        _ = btnAplicarPosicao ?? throw new InvalidOperationException("Botão Aplicar não carregado.");

        lstSsoHints.DataSource = _ssoHints;
        cmbMfaTipo.Items.AddRange(new object[] { "TOTP", "Manual" });
        cmbMfaTipo.SelectedIndex = 0;
    }

    public event EventHandler<string>? TestLoginRequested;
    public event EventHandler<string>? ApplyPositionRequested;

    public void BindSite(SiteConfig? site)
    {
        _site = site;
        txtTotpSecretKeyRef.Clear();
        _ssoHints.Clear();

        if (site is null)
        {
            Enabled = false;
            return;
        }

        Enabled = true;
        txtTotpSecretKeyRef.Text = string.IsNullOrEmpty(site.Id)
            ? string.Empty
            : Mieruka.Core.Security.CredentialVault.BuildTotpKey(site.Id);

        foreach (var hint in site.Login?.SsoHints ?? Array.Empty<string>())
        {
            _ssoHints.Add(hint);
        }
    }

    private async void btnDetectarCampos_Click(object? sender, EventArgs e)
    {
        await DetectarCamposAsync().ConfigureAwait(false);
    }

    private async Task DetectarCamposAsync()
    {
        if (_site is null)
        {
            MessageBox.Show(this, "Selecione um site para detectar campos.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            btnDetectarCampos.Enabled = false;
            await Task.Delay(250).ConfigureAwait(true);
            MessageBox.Show(this, "Detecção automática não está disponível nesta versão.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Falha ao detectar campos: {ex.Message}", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            btnDetectarCampos.Enabled = true;
        }
    }

    private void btnAdicionarSsoHint_Click(object? sender, EventArgs e)
    {
        var texto = txtSsoHintEntrada.Text.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            return;
        }

        if (!_ssoHints.Contains(texto))
        {
            _ssoHints.Add(texto);
        }

        txtSsoHintEntrada.Clear();
    }

    private void btnRemoverSsoHint_Click(object? sender, EventArgs e)
    {
        if (lstSsoHints.SelectedItem is string hint)
        {
            _ssoHints.Remove(hint);
        }
    }

    private void btnTestarLogin_Click(object? sender, EventArgs e)
    {
        if (_site is null)
        {
            MessageBox.Show(this, "Selecione um site antes de testar.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var sucesso = _orchestrator.EnsureLoggedIn(_site);
            var mensagem = sucesso ? "Login bem-sucedido." : "Falha ao efetuar login.";
            var icone = sucesso ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
            MessageBox.Show(this, mensagem, "Login Automático", MessageBoxButtons.OK, icone);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao testar login: {ex.Message}", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        TestLoginRequested?.Invoke(this, _site.Id);
    }

    private void btnAplicarPosicao_Click(object? sender, EventArgs e)
    {
        if (_site is null)
        {
            MessageBox.Show(this, "Selecione um site antes de aplicar a posição.", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            WindowMover.Apply(_site.Window);
            ApplyPositionRequested?.Invoke(this, _site.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao aplicar posição: {ex.Message}", "Login Automático", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
