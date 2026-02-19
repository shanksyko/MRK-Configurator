#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Security.Models;
using Mieruka.Core.Security.Services;
using Serilog;

namespace Mieruka.App.Forms.Security;

/// <summary>
/// Dialog used to change a user's password, typically triggered on first login when
/// <see cref="User.MustChangePassword"/> is <c>true</c>.
/// </summary>
internal sealed class ChangePasswordForm : Form
{
    private readonly IAuthenticationService _authService;
    private readonly User _user;

    private readonly TextBox _currentPasswordBox;
    private readonly TextBox _newPasswordBox;
    private readonly TextBox _confirmPasswordBox;
    private readonly Label _statusLabel;
    private readonly Button _changeButton;
    private readonly Button _cancelButton;

    public ChangePasswordForm(IAuthenticationService authService, User user)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _user = user ?? throw new ArgumentNullException(nameof(user));

        Text = "Alterar Senha";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new System.Drawing.Size(380, 260);
        ShowInTaskbar = false;
        DoubleBuffered = true;

        var currentLabel = new Label
        {
            Text = "Senha atual:",
            Location = new System.Drawing.Point(16, 20),
            AutoSize = true,
        };

        _currentPasswordBox = new TextBox
        {
            Location = new System.Drawing.Point(16, 42),
            Width = 340,
            UseSystemPasswordChar = true,
        };

        var newLabel = new Label
        {
            Text = "Nova senha:",
            Location = new System.Drawing.Point(16, 74),
            AutoSize = true,
        };

        _newPasswordBox = new TextBox
        {
            Location = new System.Drawing.Point(16, 96),
            Width = 340,
            UseSystemPasswordChar = true,
        };

        var confirmLabel = new Label
        {
            Text = "Confirmar nova senha:",
            Location = new System.Drawing.Point(16, 128),
            AutoSize = true,
        };

        _confirmPasswordBox = new TextBox
        {
            Location = new System.Drawing.Point(16, 150),
            Width = 340,
            UseSystemPasswordChar = true,
        };

        _statusLabel = new Label
        {
            Location = new System.Drawing.Point(16, 184),
            AutoSize = true,
            ForeColor = System.Drawing.Color.Red,
            Visible = false,
        };

        _changeButton = new Button
        {
            Text = "Alterar",
            Location = new System.Drawing.Point(190, 215),
            Width = 80,
        };
        _changeButton.Click += OnChangeClicked;

        _cancelButton = new Button
        {
            Text = "Cancelar",
            Location = new System.Drawing.Point(276, 215),
            Width = 80,
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = _changeButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[]
        {
            currentLabel, _currentPasswordBox,
            newLabel, _newPasswordBox,
            confirmLabel, _confirmPasswordBox,
            _statusLabel,
            _changeButton, _cancelButton,
        });
    }

    private async void OnChangeClicked(object? sender, EventArgs e)
    {
        var currentPassword = _currentPasswordBox.Text;
        var newPassword = _newPasswordBox.Text;
        var confirmPassword = _confirmPasswordBox.Text;

        if (string.IsNullOrEmpty(currentPassword))
        {
            ShowStatus("Informe a senha atual.");
            return;
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            ShowStatus("Informe a nova senha.");
            return;
        }

        if (newPassword.Length < 6)
        {
            ShowStatus("A nova senha deve ter no mínimo 6 caracteres.");
            return;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            ShowStatus("A confirmação não corresponde à nova senha.");
            return;
        }

        _changeButton.Enabled = false;
        _statusLabel.ForeColor = System.Drawing.SystemColors.ControlText;
        ShowStatus("Alterando senha...");

        try
        {
            var success = await _authService.ChangePasswordAsync(_user.Id, currentPassword, newPassword);

            if (success)
            {
                Log.Information("Senha alterada com sucesso para o usuário {Username}.", _user.Username);
                MessageBox.Show(this, "Senha alterada com sucesso.", "Alterar Senha", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _statusLabel.ForeColor = System.Drawing.Color.Red;
                ShowStatus("Senha atual incorreta ou falha na alteração.");
                _changeButton.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Falha ao alterar senha.");
            _statusLabel.ForeColor = System.Drawing.Color.Red;
            ShowStatus($"Erro: {ex.Message}");
            _changeButton.Enabled = true;
        }
    }

    private void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
    }
}
