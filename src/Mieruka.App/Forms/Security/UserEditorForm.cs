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

    private readonly Label lblUsername = new();
    private readonly TextBox txtUsername = new();
    private readonly Label lblPassword = new();
    private readonly TextBox txtPassword = new();
    private readonly Label lblPasswordHint = new();
    private readonly Label lblFullName = new();
    private readonly TextBox txtFullName = new();
    private readonly Label lblEmail = new();
    private readonly TextBox txtEmail = new();
    private readonly Label lblRole = new();
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
        ClientSize = new Size(400, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        int y = 20;
        AddField(lblUsername, "Usuário:", txtUsername, ref y);
        txtUsername.Enabled = _existingUser == null; // can't rename

        AddField(lblPassword, "Senha:", txtPassword, ref y);
        txtPassword.UseSystemPasswordChar = true;

        lblPasswordHint.AutoSize = true;
        lblPasswordHint.ForeColor = Color.Gray;
        lblPasswordHint.Font = new Font("Segoe UI", 8F);
        lblPasswordHint.Location = new Point(120, y - 12);
        lblPasswordHint.Text = _existingUser == null
            ? "(obrigatório, mín. 6 chars)"
            : "(deixe em branco para não alterar)";
        Controls.Add(lblPasswordHint);
        y += 6;

        AddField(lblFullName, "Nome Completo:", txtFullName, ref y);
        AddField(lblEmail, "E-mail:", txtEmail, ref y);

        lblRole.AutoSize = true;
        lblRole.Location = new Point(20, y);
        lblRole.Text = "Perfil:";
        Controls.Add(lblRole);

        cmbRole.Location = new Point(120, y - 3);
        cmbRole.Size = new Size(260, 23);
        cmbRole.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var role in Enum.GetValues<UserRole>())
            cmbRole.Items.Add(role.ToString());
        cmbRole.SelectedIndex = 0;
        Controls.Add(cmbRole);
        y += 40;

        btnSave.BackColor = Color.FromArgb(0, 120, 215);
        btnSave.FlatStyle = FlatStyle.Flat;
        btnSave.ForeColor = Color.White;
        btnSave.Location = new Point(120, y);
        btnSave.Size = new Size(120, 30);
        btnSave.Text = "Salvar";
        btnSave.UseVisualStyleBackColor = false;
        btnSave.Click += async (_, _) => await SaveAsync();
        Controls.Add(btnSave);

        btnCancel.Location = new Point(260, y);
        btnCancel.Size = new Size(120, 30);
        btnCancel.Text = "Cancelar";
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void AddField(Label lbl, string labelText, TextBox txt, ref int y)
    {
        lbl.AutoSize = true;
        lbl.Location = new Point(20, y + 3);
        lbl.Text = labelText;
        Controls.Add(lbl);

        txt.Location = new Point(120, y);
        txt.Size = new Size(260, 23);
        Controls.Add(txt);
        y += 35;
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
        ClientSize = new Size(340, 160);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblPass = new Label { AutoSize = true, Location = new Point(20, 23), Text = "Nova Senha:" };
        txtPassword.Location = new Point(120, 20);
        txtPassword.Size = new Size(200, 23);
        txtPassword.UseSystemPasswordChar = true;

        var lblConf = new Label { AutoSize = true, Location = new Point(20, 58), Text = "Confirmar:" };
        txtConfirm.Location = new Point(120, 55);
        txtConfirm.Size = new Size(200, 23);
        txtConfirm.UseSystemPasswordChar = true;

        btnOk.BackColor = Color.FromArgb(0, 120, 215);
        btnOk.FlatStyle = FlatStyle.Flat;
        btnOk.ForeColor = Color.White;
        btnOk.Location = new Point(60, 100);
        btnOk.Size = new Size(100, 30);
        btnOk.Text = "Confirmar";
        btnOk.UseVisualStyleBackColor = false;
        btnOk.Click += BtnOk_Click;

        btnCancel.Location = new Point(180, 100);
        btnCancel.Size = new Size(100, 30);
        btnCancel.Text = "Cancelar";
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { lblPass, txtPassword, lblConf, txtConfirm, btnOk, btnCancel });
        AcceptButton = btnOk;
        CancelButton = btnCancel;
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
