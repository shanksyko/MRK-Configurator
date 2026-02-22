#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Automation.Login;
using Mieruka.Core.Interop;
using Mieruka.Core.Models;
using Mieruka.Core.Security;
using Mieruka.App.Services;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

internal sealed partial class LoginAutoTab : WinForms.UserControl
{
    private readonly BindingList<string> _ssoHints = new();
    private LoginOrchestrator? _orchestrator;
    private SecretsProvider? _secretsProvider;
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

    /// <summary>
    /// Sets the <see cref="SecretsProvider"/> used to resolve credentials during login tests.
    /// </summary>
    public void SetSecretsProvider(SecretsProvider? provider)
    {
        _secretsProvider = provider;
        _orchestrator = provider is not null
            ? new LoginOrchestrator(provider)
            : null;
    }

    public void BindSite(SiteConfig? site)
    {
        _site = site;
        txtTotpSecretKeyRef.Clear();
        txtUserSelector.Clear();
        txtPasswordSelector.Clear();
        txtSubmitSelector.Clear();
        txtPostSubmitSelector.Clear();
        txtExtraWaitSelectors.Clear();
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

        // Load selectors from the login profile.
        if (site.Login is { } login)
        {
            txtUserSelector.Text = login.UserSelector ?? string.Empty;
            txtPasswordSelector.Text = login.PassSelector ?? string.Empty;
            txtSubmitSelector.Text = login.SubmitSelector ?? string.Empty;

            foreach (var hint in login.SsoHints ?? Array.Empty<string>())
            {
                _ssoHints.Add(hint);
            }
        }
    }

    /// <summary>
    /// Collects the current selector values and returns a <see cref="LoginProfile"/>.
    /// Returns <c>null</c> when no site is bound.
    /// </summary>
    public LoginProfile? CollectLoginProfile()
    {
        if (_site is null)
        {
            return null;
        }

        var userSelector = NullIfEmpty(txtUserSelector.Text);
        var passSelector = NullIfEmpty(txtPasswordSelector.Text);
        var submitSelector = NullIfEmpty(txtSubmitSelector.Text);
        var script = _site.Login?.Script;

        return new LoginProfile
        {
            UserSelector = userSelector,
            PassSelector = passSelector,
            SubmitSelector = submitSelector,
            Script = script,
            TimeoutSeconds = _site.Login?.TimeoutSeconds ?? 30,
            SsoHints = new List<string>(_ssoHints),
        };
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async void btnDetectarCampos_Click(object? sender, EventArgs e)
    {
        await DetectarCamposAsync();
    }

    private async Task DetectarCamposAsync()
    {
        if (_site is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um site para detectar campos.", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        try
        {
            btnDetectarCampos.Enabled = false;
            await Task.Delay(250).ConfigureAwait(true);
            WinForms.MessageBox.Show(this, "Detecção automática não está disponível nesta versão.", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, $"Falha ao detectar campos: {ex.Message}", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
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

    private async void btnTestarLogin_Click(object? sender, EventArgs e)
    {
        if (_site is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um site antes de testar.", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        if (_orchestrator is null)
        {
            WinForms.MessageBox.Show(this, "Serviço de credenciais não disponível.", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            return;
        }

        btnTestarLogin.Enabled = false;
        try
        {
            var browserArgs = BrowserArgumentBuilder.CollectBrowserArguments(_site);
            var sucesso = await _orchestrator.EnsureLoggedInAsync(_site, browserArgs).ConfigureAwait(true);
            var mensagem = sucesso ? "Login bem-sucedido." : "Falha ao efetuar login.";
            var icone = sucesso ? WinForms.MessageBoxIcon.Information : WinForms.MessageBoxIcon.Warning;
            WinForms.MessageBox.Show(this, mensagem, "Login Automático", WinForms.MessageBoxButtons.OK, icone);

            if (sucesso)
            {
                TestLoginRequested?.Invoke(this, _site.Id);
            }
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, $"Erro ao testar login: {ex.Message}", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
        }
        finally
        {
            btnTestarLogin.Enabled = true;
        }
    }

    private void btnAplicarPosicao_Click(object? sender, EventArgs e)
    {
        if (_site is null)
        {
            WinForms.MessageBox.Show(this, "Selecione um site antes de aplicar a posição.", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            return;
        }

        try
        {
            WindowMover.Apply(_site.Window);
            ApplyPositionRequested?.Invoke(this, _site.Id);
        }
        catch (Exception ex)
        {
            WinForms.MessageBox.Show(this, $"Erro ao aplicar posição: {ex.Message}", "Login Automático", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
        }
    }
}
