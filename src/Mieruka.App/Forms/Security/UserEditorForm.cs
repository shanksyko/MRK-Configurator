#nullable enable
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Security.Models;
using Mieruka.Core.Security.Services;
using Serilog;

namespace Mieruka.App.Forms.Security;

/// <summary>Dialog for creating or editing a user.</summary>
public sealed class UserEditorForm : Form
{
    private static readonly ILogger Logger = Log.ForContext<UserEditorForm>();

    private readonly IUserManagementService _userService;
    private readonly User? _existingUser;

    private readonly TextBox txtUsername = new();
    private readonly TextBox txtPassword = new();
    private readonly Label lblPasswordHint = new();
    private readonly TextBox txtFullName = new();
    private readonly TextBox txtEmail = new();
    private readonly ComboBox cmbRole = new();
    private readonly Button btnSave = new();
    private readonly Button btnCancel = new();

    public UserEditorForm(IUserManagementService userService, User? existingUser = null)
    {
        _userService = userService;
        _existingUser = existingUser;
        BuildLayout();
        if (existingUser != null)
            FillExistingValues(existingUser);
    }

    private void BuildLayout()
    {
        Text = _existingUser == null ? "Novo Usuário" : "Editar Usuário";
        MinimumSize = new Size(400, 320);
        ClientSize = new Size(420, 330);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        txtUsername.Dock = DockStyle.Fill;
        txtUsername.Enabled = _existingUser == null;
        AddRow(layout, "Usuário:", txtUsername);

        txtPassword.Dock = DockStyle.Fill;
        txtPassword.UseSystemPasswordChar = true;
        AddRow(layout, "Senha:", txtPassword);

        lblPasswordHint.AutoSize = true;
        lblPasswordHint.ForeColor = Color.Gray;
        lblPasswordHint.Font = new Font("Segoe UI", 8F);
        lblPasswordHint.Text = _existingUser == null
            ? "(obrigatório, mín. 6 chars)"
            : "(deixe em branco para não alterar)";
        var rowHint = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true }, 0, rowHint);
        layout.Controls.Add(lblPasswordHint, 1, rowHint);

        txtFullName.Dock = DockStyle.Fill;
        AddRow(layout, "Nome Completo:", txtFullName);

        txtEmail.Dock = DockStyle.Fill;
        AddRow(layout, "E-mail:", txtEmail);

        cmbRole.Dock = DockStyle.Fill;
        cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var role in Enum.GetValues<UserRole>())
            cmbRole.Items.Add(role.ToString());
        cmbRole.SelectedIndex = 0;
        AddRow(layout, "Perfil:", cmbRole);

        // Button bar
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        btnCancel.Text = "Cancelar";
        btnCancel.AutoSize = true;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnSave.BackColor = Color.FromArgb(0, 120, 215);
        btnSave.FlatStyle = FlatStyle.Flat;
        btnSave.ForeColor = Color.White;
        btnSave.Text = "Salvar";
        btnSave.AutoSize = true;
        btnSave.UseVisualStyleBackColor = false;
        btnSave.Click += async (_, _) => await SaveAsync();
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnSave);

        Controls.Add(layout);
        Controls.Add(buttonPanel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 6),
        }, 0, row);
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private void FillExistingValues(User user)
    {
        txtUsername.Text = user.Username;
        txtFullName.Text = user.FullName ?? "";
        txtEmail.Text = user.Email ?? "";
        cmbRole.SelectedIndex = (int)user.Role;
    }

    private async Task SaveAsync()
    {
        btnSave.Enabled = false;
        try
        {
            var role = (UserRole)cmbRole.SelectedIndex;

            if (_existingUser == null)
            {
                var (ok, err) = await _userService.CreateUserAsync(
                    txtUsername.Text.Trim(),
                    txtPassword.Text,
                    role,
                    string.IsNullOrWhiteSpace(txtFullName.Text) ? null : txtFullName.Text.Trim(),
                    string.IsNullOrWhiteSpace(txtEmail.Text) ? null : txtEmail.Text.Trim());

                if (!ok)
                {
                    MessageBox.Show(err ?? "Erro ao criar usuário.", "Erro",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                // Update user - optionally also reset password
                var (ok, err) = await _userService.UpdateUserAsync(
                    _existingUser.Id,
                    role,
                    string.IsNullOrWhiteSpace(txtFullName.Text) ? null : txtFullName.Text.Trim(),
                    string.IsNullOrWhiteSpace(txtEmail.Text) ? null : txtEmail.Text.Trim(),
                    _existingUser.IsActive);

                if (!ok)
                {
                    MessageBox.Show(err ?? "Erro ao atualizar usuário.", "Erro",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao salvar usuário.");
            MessageBox.Show($"Erro ao salvar: {ex.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            btnSave.Enabled = true;
        }
    }
}

/// <summary>Simple dialog for entering a new password when resetting.</summary>
public sealed class ResetPasswordForm : Form
{
    private readonly TextBox txtPassword = new();
    private readonly TextBox txtConfirm = new();
    private readonly Button btnOk = new();
    private readonly Button btnCancel = new();

    public string NewPassword => txtPassword.Text;

    public ResetPasswordForm()
    {
        Text = "Definir Nova Senha";
        MinimumSize = new Size(340, 180);
        ClientSize = new Size(360, 180);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));

        txtPassword.Dock = DockStyle.Fill;
        txtPassword.UseSystemPasswordChar = true;
        AddRow(layout, "Nova Senha:", txtPassword);

        txtConfirm.Dock = DockStyle.Fill;
        txtConfirm.UseSystemPasswordChar = true;
        AddRow(layout, "Confirmar:", txtConfirm);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        btnCancel.Text = "Cancelar";
        btnCancel.AutoSize = true;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnOk.BackColor = Color.FromArgb(0, 120, 215);
        btnOk.FlatStyle = FlatStyle.Flat;
        btnOk.ForeColor = Color.White;
        btnOk.Text = "Confirmar";
        btnOk.AutoSize = true;
        btnOk.UseVisualStyleBackColor = false;
        btnOk.Click += BtnOk_Click;
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOk);

        Controls.Add(layout);
        Controls.Add(buttonPanel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control control)
    {
        var row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 0, 6, 6),
        }, 0, row);
        control.Margin = new Padding(0, 0, 0, 6);
        panel.Controls.Add(control, 1, row);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (txtPassword.Text.Length < 6)
        {
            MessageBox.Show("Senha deve ter pelo menos 6 caracteres.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (txtPassword.Text != txtConfirm.Text)
        {
            MessageBox.Show("As senhas não coincidem.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
