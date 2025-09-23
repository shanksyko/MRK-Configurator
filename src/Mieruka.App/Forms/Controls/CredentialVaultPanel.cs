using System;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms.Controls;

public sealed class CredentialVaultPanel : UserControl
{
    private readonly SecretsProvider? _secretsProvider;
    private readonly UiSecretsBridge? _secretsBridge;
    private readonly Label _scopeLabel;
    private readonly Label _statusLabel;
    private readonly TextBox _userBox;
    private readonly TextBox _passwordBox;
    private readonly TextBox _totpBox;

    internal readonly Button btnSalvar;
    internal readonly Button btnApagar;
    internal readonly Button btnTestar;

    private string? _scopeSiteId;

    public event EventHandler<string>? TestLoginRequested;

    public CredentialVaultPanel()
    {
        LayoutHelpers.ApplyStandardLayout(this);
        AutoScroll = true;

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

        var layout = LayoutHelpers.CreateStandardTableLayout();
        layout.RowCount = 6;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _scopeLabel = new Label { AutoSize = true };
        layout.Controls.Add(_scopeLabel, 0, 0);

        _statusLabel = new Label { AutoSize = true };
        layout.Controls.Add(_statusLabel, 0, 1);

        _userBox = CreateSecretTextBox("Usuário");
        layout.Controls.Add(_userBox, 0, 2);

        _passwordBox = CreateSecretTextBox("Senha");
        layout.Controls.Add(_passwordBox, 0, 3);

        _totpBox = CreateSecretTextBox("TOTP");
        layout.Controls.Add(_totpBox, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };

        btnSalvar = new Button { Text = "Salvar", AutoSize = true };
        btnSalvar.Click += btnSalvar_Click;
        buttons.Controls.Add(btnSalvar);

        btnApagar = new Button { Text = "Apagar", AutoSize = true };
        btnApagar.Click += btnApagar_Click;
        buttons.Controls.Add(btnApagar);

        btnTestar = new Button { Text = "Testar Login", AutoSize = true };
        btnTestar.Click += btnTestar_Click;
        buttons.Controls.Add(btnTestar);

        layout.Controls.Add(buttons, 0, 5);

        Controls.Add(layout);

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

    private static TextBox CreateSecretTextBox(string placeholder)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = placeholder,
        };
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
            _secretsBridge!.Save(_scopeSiteId!, _userBox, _passwordBox, _totpBox);
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

        var confirmation = MessageBox.Show(
            this,
            "Deseja realmente remover as credenciais?",
            "Credential Vault",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmation != DialogResult.Yes)
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
        _userBox.Enabled = hasScope;
        _passwordBox.Enabled = hasScope;
        _totpBox.Enabled = hasScope;

        if (!hasScope)
        {
            _scopeLabel.Text = "Selecione um site";
            _statusLabel.Text = string.Empty;
            ClearInputs();
            UpdateButtonsAvailability();
            return;
        }

        _scopeLabel.Text = $"Credenciais para: {_scopeSiteId}";
        UpdateStatus();
        UpdateButtonsAvailability();
    }

    private void UpdateStatus()
    {
        if (!EnsureScopeAndBridge())
        {
            _statusLabel.Text = "Cofre indisponível";
            return;
        }

        using var username = _secretsBridge!.LoadUser(_scopeSiteId!);
        using var password = _secretsBridge.LoadPass(_scopeSiteId!);
        using var totp = _secretsBridge.LoadTotp(_scopeSiteId!);

        var hasUser = username is { Length: > 0 };
        var hasPassword = password is { Length: > 0 };
        var hasTotp = totp is { Length: > 0 };

        _statusLabel.Text =
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
            MessageBox.Show(
                this,
                "Serviços de credencial indisponíveis nesta instalação.",
                "Credential Vault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
        _userBox.Clear();
        _passwordBox.Clear();
        _totpBox.Clear();
    }
}
