#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Security.Services;
using Mieruka.Core.Security.Models;
using Serilog;

namespace Mieruka.App.Forms.Security;

public partial class LoginForm : Form
{
    private readonly IAuthenticationService _authService;
    private User? _authenticatedUser;

    public User? AuthenticatedUser => _authenticatedUser;
    public bool LoginSuccessful { get; private set; }

    public LoginForm(IAuthenticationService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        InitializeComponent();
    }

    private void LoginForm_Load(object? sender, EventArgs e)
    {
        txtPassword.UseSystemPasswordChar = true;
        txtUsername.Focus();
    }

    private async void btnLogin_Click(object? sender, EventArgs e)
    {
        var username = txtUsername.Text.Trim();
        var password = txtPassword.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            MessageBox.Show("Por favor, insira usuário e senha.", "Login", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnLogin.Enabled = false;
        lblStatus.Text = "Autenticando...";
        lblStatus.Visible = true;

        try
        {
            var (success, user, errorMessage) = await _authService.AuthenticateAsync(username, password);

            if (success && user != null)
            {
                _authenticatedUser = user;
                LoginSuccessful = true;

                Log.Information("Login bem-sucedido: {Username}", username);

                if (user.MustChangePassword)
                {
                    MessageBox.Show("Você deve alterar sua senha no primeiro login.", "Alterar Senha", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    // TODO: Implement ChangePasswordForm
                    // using var changePasswordForm = new ChangePasswordForm(_authService, user);
                    // if (changePasswordForm.ShowDialog() == DialogResult.OK) { ... }
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            else
            {
                Log.Warning("Falha no login: {Username}, Erro: {Error}", username, errorMessage);
                MessageBox.Show(errorMessage ?? "Falha na autenticação.", "Erro de Login", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Falha na autenticação.";
                txtPassword.Clear();
                txtPassword.Focus();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erro durante autenticação: {Username}", username);
            MessageBox.Show($"Erro durante autenticação: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            lblStatus.Text = "Erro no sistema.";
        }
        finally
        {
            btnLogin.Enabled = true;
        }
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void txtPassword_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            btnLogin.PerformClick();
        }
    }

    private void txtUsername_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            e.Handled = true;
            txtPassword.Focus();
        }
    }
}
