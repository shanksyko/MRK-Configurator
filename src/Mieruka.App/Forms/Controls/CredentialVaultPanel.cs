#nullable enable
using System;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Security;
using WinForms = System.Windows.Forms;

namespace Mieruka.App.Forms.Controls;

public sealed partial class CredentialVaultPanel : WinForms.UserControl
{
    private readonly SecretsProvider? _secretsProvider;
    private readonly UiSecretsBridge? _secretsBridge;
    private string? _scopeSiteId;

    public event EventHandler<string>? TestLoginRequested;

    public CredentialVaultPanel()
    {
        InitializeComponent();

        _ = layoutPrincipal ?? throw new InvalidOperationException("Layout não carregado.");
        _ = lblScope ?? throw new InvalidOperationException("Label scope não carregado.");
        _ = lblStatus ?? throw new InvalidOperationException("Label status não carregado.");
        _ = txtUsuario ?? throw new InvalidOperationException("Campo usuário não carregado.");
        _ = txtSenha ?? throw new InvalidOperationException("Campo senha não carregado.");
        _ = txtTotp ?? throw new InvalidOperationException("Campo TOTP não carregado.");
        _ = btnSalvar ?? throw new InvalidOperationException("Botão salvar não carregado.");
        _ = btnApagar ?? throw new InvalidOperationException("Botão apagar não carregado.");
        _ = btnTestar ?? throw new InvalidOperationException("Botão testar não carregado.");

        try
        {
            var vault = new Mieruka.Core.Security.CredentialVault();
            var cookies = new CookieSafeStore();
            _secretsProvider = new SecretsProvider(vault, cookies);
            _secretsBridge = new UiSecretsBridge(_secretsProvider);
            _secretsProvider.CredentialsChanged += OnCredentialsChanged;
        }
        catch (Exception)
        {
            _secretsProvider = null;
            _secretsBridge = null;
        }

        Enabled = false;
        UpdateScope();
        UpdateButtonsAvailability();
    }

    public string? ScopeSiteId
    {
        get => _scopeSiteId;
        set
        {
            if (string.Equals(_scopeSiteId, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _scopeSiteId = value;
            UpdateScope();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _secretsProvider is not null)
        {
            _secretsProvider.CredentialsChanged -= OnCredentialsChanged;
        }

        base.Dispose(disposing);
    }

    private void btnSalvar_Click(object? sender, EventArgs e)
    {
        if (!EnsureScopeAndBridge())
        {
            return;
        }

        btnSalvar.Enabled = false;
        try
        {
            _secretsBridge!.Save(_scopeSiteId!, txtUsuario, txtSenha, txtTotp);
            UpdateStatus();
        }
        finally
        {
            UpdateButtonsAvailability();
        }
    }

    private void btnApagar_Click(object? sender, EventArgs e)
    {
        if (!EnsureScopeAndBridge())
        {
            return;
        }

        var confirmation = WinForms.MessageBox.Show(
            this,
            "Deseja realmente remover as credenciais?",
            "Credential Vault",
            WinForms.MessageBoxButtons.YesNo,
            WinForms.MessageBoxIcon.Question);

        if (confirmation != WinForms.DialogResult.Yes)
        {
            return;
        }

        _secretsBridge!.Delete(_scopeSiteId!);
        ClearInputs();
        UpdateStatus();
    }

    private void btnTestar_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_scopeSiteId))
        {
            return;
        }

        TestLoginRequested?.Invoke(this, _scopeSiteId);
    }

    private void UpdateScope()
    {
        var hasScope = !string.IsNullOrWhiteSpace(_scopeSiteId);
        Enabled = hasScope;
        txtUsuario.Enabled = hasScope;
        txtSenha.Enabled = hasScope;
        txtTotp.Enabled = hasScope;

        if (!hasScope)
        {
            lblScope.Text = "Selecione um site";
            lblStatus.Text = string.Empty;
            ClearInputs();
            UpdateButtonsAvailability();
            return;
        }

        lblScope.Text = $"Credenciais para: {_scopeSiteId}";
        UpdateStatus();
        UpdateButtonsAvailability();
    }

    private void UpdateStatus()
    {
        if (!EnsureScopeAndBridge())
        {
            lblStatus.Text = "Cofre indisponível";
            return;
        }

        using var username = _secretsBridge!.LoadUser(_scopeSiteId!);
        using var password = _secretsBridge.LoadPass(_scopeSiteId!);
        using var totp = _secretsBridge.LoadTotp(_scopeSiteId!);

        var hasUser = username is { Length: > 0 };
        var hasPassword = password is { Length: > 0 };
        var hasTotp = totp is { Length: > 0 };

        lblStatus.Text =
            $"Usuário: {(hasUser ? "●" : "—")}, Senha: {(hasPassword ? "●" : "—")}, TOTP: {(hasTotp ? "●" : "—")}";
    }

    private bool EnsureScopeAndBridge()
    {
        if (string.IsNullOrEmpty(_scopeSiteId))
        {
            return false;
        }

        if (_secretsBridge is null)
        {
            WinForms.MessageBox.Show(
                this,
                "Serviços de credencial indisponíveis nesta instalação.",
                "Credential Vault",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return false;
        }

        return true;
    }

    private void OnCredentialsChanged(object? sender, CredentialChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(_scopeSiteId))
        {
            return;
        }

        if (string.Equals(e.SiteId, _scopeSiteId, StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus();
        }
    }

    private void UpdateButtonsAvailability()
    {
        var servicesAvailable = _secretsBridge is not null && !string.IsNullOrEmpty(_scopeSiteId);
        btnSalvar.Enabled = servicesAvailable;
        btnApagar.Enabled = servicesAvailable;
        btnTestar.Enabled = servicesAvailable;
    }

    private void ClearInputs()
    {
        txtUsuario.Clear();
        txtSenha.Clear();
        txtTotp.Clear();
    }
}
