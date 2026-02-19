#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Security.Models;
using Mieruka.Core.Security.Services;
using Serilog;

namespace Mieruka.App.Forms.Security;

public partial class UserManagementForm : Form
{
    private static readonly ILogger Logger = Log.ForContext<UserManagementForm>();

    private readonly IUserManagementService _userService;
    private readonly User _currentUser;
    private List<User> _allUsers = new();

    public UserManagementForm(IUserManagementService userService, User currentUser)
    {
        _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        InitializeComponent();
    }

    private async void UserManagementForm_Load(object? sender, EventArgs e)
    {
        SetupGrid();
        SetupRoleFilter();
        await RefreshUsersAsync();
    }

    private void SetupGrid()
    {
        dgvUsers.Columns.Clear();
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colId", HeaderText = "ID", Width = 50, FillWeight = 10 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colUsername", HeaderText = "Usuário", FillWeight = 20 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colFullName", HeaderText = "Nome Completo", FillWeight = 25 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colEmail", HeaderText = "E-mail", FillWeight = 25 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colRole", HeaderText = "Perfil", FillWeight = 15 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colActive", HeaderText = "Ativo", FillWeight = 10 });
        dgvUsers.Columns.Add(new DataGridViewTextBoxColumn { Name = "colLastLogin", HeaderText = "Último Login", FillWeight = 20 });
    }

    private void SetupRoleFilter()
    {
        cmbRoleFilter.Items.Clear();
        cmbRoleFilter.Items.Add("Todos");
        foreach (var role in Enum.GetValues<UserRole>())
            cmbRoleFilter.Items.Add(role.ToString());
        cmbRoleFilter.SelectedIndex = 0;
    }

    private async Task RefreshUsersAsync()
    {
        try
        {
            _allUsers = await _userService.GetAllUsersAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao carregar usuários.");
            MessageBox.Show($"Erro ao carregar usuários: {ex.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyFilter()
    {
        var search = txtSearch.Text.Trim().ToLowerInvariant();
        var roleFilter = cmbRoleFilter.SelectedIndex > 0
            ? (UserRole?)(UserRole)(cmbRoleFilter.SelectedIndex - 1)
            : null;

        var filtered = _allUsers
            .Where(u => roleFilter == null || u.Role == roleFilter)
            .Where(u => string.IsNullOrEmpty(search)
                || u.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (u.FullName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        dgvUsers.SuspendLayout();
        dgvUsers.Rows.Clear();
        foreach (var u in filtered)
        {
            var row = dgvUsers.Rows[dgvUsers.Rows.Add()];
            row.Cells["colId"].Value = u.Id;
            row.Cells["colUsername"].Value = u.Username;
            row.Cells["colFullName"].Value = u.FullName ?? "";
            row.Cells["colEmail"].Value = u.Email ?? "";
            row.Cells["colRole"].Value = u.Role.ToString();
            row.Cells["colActive"].Value = u.IsActive ? "Sim" : "Não";
            row.Cells["colLastLogin"].Value = u.LastLoginAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "-";
            row.Tag = u;

            if (!u.IsActive)
                row.DefaultCellStyle.ForeColor = Color.Gray;
        }
        dgvUsers.ResumeLayout();
        UpdateButtonStates();
    }

    private User? GetSelectedUser()
    {
        if (dgvUsers.SelectedRows.Count == 0) return null;
        return dgvUsers.SelectedRows[0].Tag as User;
    }

    private void UpdateButtonStates()
    {
        var selected = GetSelectedUser();
        var isAdmin = _currentUser.Role == UserRole.Admin;
        var hasSelection = selected != null;

        btnNew.Enabled = isAdmin;
        btnEdit.Enabled = isAdmin && hasSelection;
        btnDeactivate.Enabled = isAdmin && hasSelection && selected!.Id != _currentUser.Id;
        btnResetPassword.Enabled = isAdmin && hasSelection;

        if (selected != null)
        {
            btnDeactivate.Text = selected.IsActive ? "Desativar" : "Ativar";
        }
    }

    private async void btnNew_Click(object? sender, EventArgs e)
    {
        using var form = new UserEditorForm(_userService);
        if (form.ShowDialog(this) == DialogResult.OK)
            await RefreshUsersAsync();
    }

    private async void btnEdit_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        using var form = new UserEditorForm(_userService, user);
        if (form.ShowDialog(this) == DialogResult.OK)
            await RefreshUsersAsync();
    }

    private async void btnDeactivate_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        var action = user.IsActive ? "desativar" : "ativar";
        var confirm = MessageBox.Show(
            $"Deseja {action} o usuário '{user.Username}'?",
            "Confirmação", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        var (success, error) = await _userService.UpdateUserAsync(
            user.Id, user.Role, user.FullName, user.Email, !user.IsActive);

        if (success)
        {
            await RefreshUsersAsync();
        }
        else
        {
            MessageBox.Show(error ?? "Erro ao atualizar usuário.", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void btnResetPassword_Click(object? sender, EventArgs e)
    {
        var user = GetSelectedUser();
        if (user == null) return;

        var confirm = MessageBox.Show(
            $"Redefinir a senha do usuário '{user.Username}'?\n\nO usuário deverá trocar a senha no próximo login.",
            "Resetar Senha", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes) return;

        using var form = new ResetPasswordForm();
        if (form.ShowDialog(this) != DialogResult.OK) return;

        var (success, error) = await _userService.ResetPasswordAsync(
            user.Id, form.NewPassword, _currentUser.Id);

        if (success)
        {
            MessageBox.Show("Senha redefinida com sucesso.", "Senha Redefinida",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RefreshUsersAsync();
        }
        else
        {
            MessageBox.Show(error ?? "Erro ao redefinir senha.", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void txtSearch_TextChanged(object? sender, EventArgs e) => ApplyFilter();
    private void cmbRoleFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilter();

    private void dgvUsers_SelectionChanged(object? sender, EventArgs e) => UpdateButtonStates();

    private void dgvUsers_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex >= 0 && _currentUser.Role == UserRole.Admin)
            btnEdit.PerformClick();
    }
}
