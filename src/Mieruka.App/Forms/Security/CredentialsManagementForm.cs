#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mieruka.Core.Data.Services;
using Mieruka.Core.Security;
using Mieruka.Core.Security.Models;
using Serilog;

namespace Mieruka.App.Forms.Security;

/// <summary>Dialog for managing dashboard credentials stored in the database and CredentialVault.</summary>
public sealed class CredentialsManagementForm : Form
{
    private static readonly ILogger Logger = Log.ForContext<CredentialsManagementForm>();

    private readonly SecurityCrudService _crudService;
    private readonly CredentialVault _vault;
    private readonly int _currentUserId;

    private readonly ListView _listView;
    private readonly Button _btnNew;
    private readonly Button _btnEdit;
    private readonly Button _btnDelete;
    private readonly Button _btnClose;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;

    private List<DashboardCredential> _credentials = new();

    public CredentialsManagementForm(SecurityCrudService crudService, CredentialVault vault, int currentUserId)
    {
        _crudService = crudService ?? throw new ArgumentNullException(nameof(crudService));
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _currentUserId = currentUserId;

        Text = "Gerenciar Credenciais";
        ClientSize = new Size(700, 460);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;

        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Carregando...");
        _statusStrip.Items.Add(_statusLabel);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.None,
        };
        _listView.Columns.Add("Nome", 180, HorizontalAlignment.Left);
        _listView.Columns.Add("Site ID", 140, HorizontalAlignment.Left);
        _listView.Columns.Add("Usuário", 140, HorizontalAlignment.Left);
        _listView.Columns.Add("URL", 140, HorizontalAlignment.Left);
        _listView.SelectedIndexChanged += (_, _) => UpdateButtonStates();
        _listView.DoubleClick += (_, _) => OnEditClicked();

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8),
        };

        _btnNew = CreateButton("Novo", Color.FromArgb(0, 120, 215), Color.White);
        _btnNew.Click += async (_, _) => await OnNewClickedAsync();
        _btnEdit = CreateButton("Editar", Color.FromArgb(245, 245, 245), Color.FromArgb(30, 30, 30));
        _btnEdit.Click += (_, _) => OnEditClicked();
        _btnDelete = CreateButton("Excluir", Color.FromArgb(196, 43, 28), Color.White);
        _btnDelete.Click += async (_, _) => await OnDeleteClickedAsync();
        _btnClose = CreateButton("Fechar", Color.FromArgb(245, 245, 245), Color.FromArgb(30, 30, 30));
        _btnClose.Click += (_, _) => Close();

        buttonPanel.Controls.AddRange(new Control[] { _btnNew, _btnEdit, _btnDelete, _btnClose });

        Controls.Add(_listView);
        Controls.Add(buttonPanel);
        Controls.Add(_statusStrip);

        UpdateButtonStates();
        Shown += async (_, _) => await LoadAsync();
    }

    private static Button CreateButton(string text, Color back, Color fore)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(14, 5, 14, 5),
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
        return btn;
    }

    private async Task LoadAsync()
    {
        try
        {
            _credentials = new List<DashboardCredential>(await _crudService.GetAllCredentialsAsync());
            RefreshList();
            SetStatus($"{_credentials.Count} credencial(is) carregada(s).");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao carregar credenciais.");
            SetStatus("Erro ao carregar credenciais.");
        }
    }

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (var cred in _credentials)
        {
            var item = new ListViewItem(cred.DisplayName) { Tag = cred };
            item.SubItems.Add(cred.SiteId);
            item.SubItems.Add(cred.Username);
            item.SubItems.Add(cred.Url ?? string.Empty);
            _listView.Items.Add(item);
        }
        _listView.EndUpdate();
        UpdateButtonStates();
    }

    private DashboardCredential? GetSelected()
    {
        return _listView.SelectedItems.Count > 0
            ? _listView.SelectedItems[0].Tag as DashboardCredential
            : null;
    }

    private async Task OnNewClickedAsync()
    {
        using var editor = new CredentialEditorForm(null, _vault);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
        {
            return;
        }

        try
        {
            var cred = editor.Result;
            cred.CreatedBy = _currentUserId;
            cred.UpdatedBy = _currentUserId;
            await _crudService.CreateCredentialAsync(cred);

            if (!string.IsNullOrWhiteSpace(editor.PlainPassword))
            {
                _vault.SaveSecret(CredentialVault.BuildPasswordKey(cred.SiteId), editor.PlainPassword);
            }

            Logger.Information("Credencial criada: {SiteId}", cred.SiteId);
            await LoadAsync();
            SetStatus($"Credencial '{cred.DisplayName}' criada.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao criar credencial.");
            MessageBox.Show($"Erro ao criar credencial: {ex.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnEditClicked()
    {
        var selected = GetSelected();
        if (selected is null) return;

        _ = OnEditAsync(selected);
    }

    private async Task OnEditAsync(DashboardCredential existing)
    {
        using var editor = new CredentialEditorForm(existing, _vault);
        if (editor.ShowDialog(this) != DialogResult.OK || editor.Result is null)
        {
            return;
        }

        try
        {
            var cred = editor.Result;
            cred.UpdatedBy = _currentUserId;
            await _crudService.UpdateCredentialAsync(cred);

            if (!string.IsNullOrWhiteSpace(editor.PlainPassword))
            {
                _vault.SaveSecret(CredentialVault.BuildPasswordKey(cred.SiteId), editor.PlainPassword);
            }

            Logger.Information("Credencial atualizada: {SiteId}", cred.SiteId);
            await LoadAsync();
            SetStatus($"Credencial '{cred.DisplayName}' atualizada.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao atualizar credencial.");
            MessageBox.Show($"Erro ao atualizar credencial: {ex.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnDeleteClickedAsync()
    {
        var selected = GetSelected();
        if (selected is null) return;

        if (MessageBox.Show(
            $"Deseja excluir a credencial '{selected.DisplayName}'?",
            "Excluir credencial",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _crudService.DeleteCredentialAsync(selected.Id);
            _vault.DeleteSecret(CredentialVault.BuildPasswordKey(selected.SiteId));

            Logger.Information("Credencial excluída: {SiteId}", selected.SiteId);
            await LoadAsync();
            SetStatus($"Credencial '{selected.DisplayName}' excluída.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Erro ao excluir credencial.");
            MessageBox.Show($"Erro ao excluir credencial: {ex.Message}", "Erro",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateButtonStates()
    {
        var hasSelection = _listView.SelectedItems.Count > 0;
        _btnEdit.Enabled = hasSelection;
        _btnDelete.Enabled = hasSelection;
    }

    private void SetStatus(string message) => _statusLabel.Text = message;
}

/// <summary>Dialog for creating or editing a DashboardCredential.</summary>
public sealed class CredentialEditorForm : Form
{
    private readonly CredentialVault _vault;
    private readonly DashboardCredential? _existing;

    private readonly TextBox _txtDisplayName = new();
    private readonly TextBox _txtSiteId = new();
    private readonly TextBox _txtUsername = new();
    private readonly TextBox _txtPassword = new();
    private readonly TextBox _txtUrl = new();
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnCancel = new();

    public DashboardCredential? Result { get; private set; }
    public string PlainPassword { get; private set; } = string.Empty;

    public CredentialEditorForm(DashboardCredential? existing, CredentialVault vault)
    {
        _existing = existing;
        _vault = vault;

        Text = existing is null ? "Nova Credencial" : "Editar Credencial";
        ClientSize = new Size(420, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        int y = 20;
        AddField("Nome:", _txtDisplayName, ref y);
        AddField("Site ID:", _txtSiteId, ref y);
        _txtSiteId.Enabled = existing is null;
        AddField("Usuário:", _txtUsername, ref y);
        AddField("Senha:", _txtPassword, ref y);
        _txtPassword.UseSystemPasswordChar = true;

        var hintLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8F),
            Location = new Point(130, y - 12),
            Text = existing is null ? "(obrigatória)" : "(deixe em branco para não alterar)",
        };
        Controls.Add(hintLabel);
        y += 4;

        AddField("URL:", _txtUrl, ref y);
        AddField("Notas:", _txtNotes, ref y);

        _btnSave.BackColor = Color.FromArgb(0, 120, 215);
        _btnSave.FlatStyle = FlatStyle.Flat;
        _btnSave.ForeColor = Color.White;
        _btnSave.Location = new Point(130, y + 8);
        _btnSave.Size = new Size(120, 30);
        _btnSave.Text = "Salvar";
        _btnSave.UseVisualStyleBackColor = false;
        _btnSave.Click += OnSaveClicked;
        Controls.Add(_btnSave);

        _btnCancel.Location = new Point(270, y + 8);
        _btnCancel.Size = new Size(120, 30);
        _btnCancel.Text = "Cancelar";
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(_btnCancel);

        AcceptButton = _btnSave;
        CancelButton = _btnCancel;

        if (existing is not null)
        {
            FillExistingValues(existing);
        }
    }

    private void AddField(string labelText, TextBox txt, ref int y)
    {
        var lbl = new Label { AutoSize = true, Location = new Point(20, y + 3), Text = labelText };
        Controls.Add(lbl);
        txt.Location = new Point(130, y);
        txt.Size = new Size(270, 23);
        Controls.Add(txt);
        y += 35;
    }

    private void FillExistingValues(DashboardCredential cred)
    {
        _txtDisplayName.Text = cred.DisplayName;
        _txtSiteId.Text = cred.SiteId;
        _txtUsername.Text = cred.Username;
        _txtUrl.Text = cred.Url ?? string.Empty;
        _txtNotes.Text = cred.Notes ?? string.Empty;

        if (_vault.TryGet(CredentialVault.BuildPasswordKey(cred.SiteId), out var secret) && secret is not null)
        {
            _txtPassword.UseSystemPasswordChar = false;
            _txtPassword.Text = "(senha armazenada)";
            _txtPassword.ForeColor = Color.Gray;
            _txtPassword.UseSystemPasswordChar = true;
            secret.Dispose();
        }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var displayName = _txtDisplayName.Text.Trim();
        var siteId = _txtSiteId.Text.Trim();
        var username = _txtUsername.Text.Trim();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            MessageBox.Show("Informe um nome para a credencial.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(siteId))
        {
            MessageBox.Show("Informe um Site ID.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_existing is null && string.IsNullOrEmpty(_txtPassword.Text))
        {
            MessageBox.Show("Informe uma senha.", "Validação",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        PlainPassword = _txtPassword.Text;

        Result = new DashboardCredential
        {
            Id = _existing?.Id ?? 0,
            SiteId = siteId,
            DisplayName = displayName,
            Username = username,
            Url = string.IsNullOrWhiteSpace(_txtUrl.Text) ? null : _txtUrl.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim(),
            CreatedBy = _existing?.CreatedBy ?? 0,
            CreatedAt = _existing?.CreatedAt ?? DateTime.UtcNow,
            UpdatedBy = 0,
            UpdatedAt = DateTime.UtcNow,
            EncryptedPassword = _existing?.EncryptedPassword ?? Array.Empty<byte>(),
            PasswordIV = _existing?.PasswordIV ?? Array.Empty<byte>(),
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
