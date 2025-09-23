using System;
using System.Windows.Forms;
using Mieruka.App.Services;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms.Controls;

public sealed class CredentialVaultPanel : UserControl
{
    private readonly SecretsProvider _secretsProvider;
    private readonly UiSecretsBridge _secretsBridge;
    private readonly Label _scopeLabel;
    private readonly Label _statusLabel;
    private readonly TextBox _userBox;
    private readonly TextBox _passwordBox;
    private readonly TextBox _totpBox;
    private string? _scopeSiteId;

    public event EventHandler<string>? TestLoginRequested;

    public CredentialVaultPanel(SecretsProvider secretsProvider, UiSecretsBridge secretsBridge)
    {
        _secretsProvider = secretsProvider ?? throw new ArgumentNullException(nameof(secretsProvider));
        _secretsBridge = secretsBridge ?? throw new ArgumentNullException(nameof(secretsBridge));

        LayoutHelpers.ApplyStandardLayout(this);
        AutoScroll = true;

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
        };

        var saveButton = new Button { Text = "Salvar no Cofre", AutoSize = true };
        saveButton.Click += (_, _) => SaveCurrent();
        buttons.Controls.Add(saveButton);

        var deleteButton = new Button { Text = "Apagar", AutoSize = true };
        deleteButton.Click += (_, _) => DeleteCurrent();
        buttons.Controls.Add(deleteButton);

        var testButton = new Button { Text = "Testar Login", AutoSize = true };
        testButton.Click += (_, _) => TriggerTestLogin();
        buttons.Controls.Add(testButton);

        layout.Controls.Add(buttons, 0, 5);

        Controls.Add(layout);

        Enabled = false;
        UpdateScope();
        _secretsProvider.CredentialsChanged += OnCredentialsChanged;
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
        if (disposing)
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

    private void UpdateScope()
    {
        var hasScope = !string.IsNullOrWhiteSpace(_scopeSiteId);
        Enabled = hasScope;
        _userBox.Enabled = hasScope;
        _passwordBox.Enabled = hasScope;
        _totpBox.Enabled = hasScope;

        if (hasScope)
        {
            _scopeLabel.Text = $"Credenciais para: {_scopeSiteId}";
            UpdateStatus();
        }
        else
        {
            _scopeLabel.Text = "Selecione um site";
            _statusLabel.Text = string.Empty;
            ClearInputs();
        }
    }

    private void UpdateStatus()
    {
        if (string.IsNullOrEmpty(_scopeSiteId))
        {
            _statusLabel.Text = string.Empty;
            return;
        }

        using var username = _secretsBridge.LoadUser(_scopeSiteId);
        using var password = _secretsBridge.LoadPass(_scopeSiteId);
        using var totp = _secretsBridge.LoadTotp(_scopeSiteId);

        var hasUser = username is { Length: > 0 };
        var hasPassword = password is { Length: > 0 };
        var hasTotp = totp is { Length: > 0 };

        _statusLabel.Text =
            $"Usuário: {(hasUser ? "●" : "—")}, Senha: {(hasPassword ? "●" : "—")}, TOTP: {(hasTotp ? "●" : "—")}";
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrEmpty(_scopeSiteId))
        {
            return;
        }

        _secretsBridge.Save(_scopeSiteId, _userBox, _passwordBox, _totpBox);
        UpdateStatus();
    }

    private void DeleteCurrent()
    {
        if (string.IsNullOrEmpty(_scopeSiteId))
        {
            return;
        }

        _secretsBridge.Delete(_scopeSiteId);
        ClearInputs();
        UpdateStatus();
    }

    private void TriggerTestLogin()
    {
        if (!string.IsNullOrEmpty(_scopeSiteId))
        {
            TestLoginRequested?.Invoke(this, _scopeSiteId);
        }
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

    private void ClearInputs()
    {
        _userBox.Clear();
        _passwordBox.Clear();
        _totpBox.Clear();
    }
}
