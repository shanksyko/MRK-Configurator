#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Security;

partial class UserManagementForm
{
    private System.ComponentModel.IContainer components = null!;
    private DataGridView dgvUsers = null!;
    private Panel panelTop = null!;
    private Panel panelBottom = null!;
    private Button btnNew = null!;
    private Button btnEdit = null!;
    private Button btnDeactivate = null!;
    private Button btnResetPassword = null!;
    private Button btnClose = null!;
    private TextBox txtSearch = null!;
    private ComboBox cmbRoleFilter = null!;
    private Label lblSearch = null!;
    private Label lblRoleFilter = null!;
    private Label lblTitle = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        panelTop = new Panel();
        panelBottom = new Panel();
        dgvUsers = new DataGridView();
        btnNew = new Button();
        btnEdit = new Button();
        btnDeactivate = new Button();
        btnResetPassword = new Button();
        btnClose = new Button();
        txtSearch = new TextBox();
        cmbRoleFilter = new ComboBox();
        lblSearch = new Label();
        lblRoleFilter = new Label();
        lblTitle = new Label();

        panelTop.SuspendLayout();
        panelBottom.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvUsers).BeginInit();
        SuspendLayout();

        // panelTop
        panelTop.BackColor = Color.FromArgb(240, 240, 240);
        panelTop.Controls.Add(lblTitle);
        panelTop.Controls.Add(lblSearch);
        panelTop.Controls.Add(txtSearch);
        panelTop.Controls.Add(lblRoleFilter);
        panelTop.Controls.Add(cmbRoleFilter);
        panelTop.Dock = DockStyle.Top;
        panelTop.Height = 80;
        panelTop.Padding = new Padding(10, 8, 10, 8);

        // lblTitle
        lblTitle.AutoSize = false;
        lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        lblTitle.Location = new Point(10, 8);
        lblTitle.Size = new Size(300, 24);
        lblTitle.Text = "Gerenciamento de Usuários";

        // lblSearch
        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(10, 48);
        lblSearch.Text = "Buscar:";

        // txtSearch
        txtSearch.Location = new Point(60, 44);
        txtSearch.Size = new Size(200, 23);
        txtSearch.TextChanged += txtSearch_TextChanged;

        // lblRoleFilter
        lblRoleFilter.AutoSize = true;
        lblRoleFilter.Location = new Point(280, 48);
        lblRoleFilter.Text = "Perfil:";

        // cmbRoleFilter
        cmbRoleFilter.Location = new Point(320, 44);
        cmbRoleFilter.Size = new Size(150, 23);
        cmbRoleFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRoleFilter.SelectedIndexChanged += cmbRoleFilter_SelectedIndexChanged;

        // dgvUsers
        dgvUsers.AllowUserToAddRows = false;
        dgvUsers.AllowUserToDeleteRows = false;
        dgvUsers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvUsers.BackgroundColor = Color.White;
        dgvUsers.BorderStyle = BorderStyle.None;
        dgvUsers.Dock = DockStyle.Fill;
        dgvUsers.GridColor = Color.FromArgb(224, 224, 224);
        dgvUsers.MultiSelect = false;
        dgvUsers.ReadOnly = true;
        dgvUsers.RowHeadersVisible = false;
        dgvUsers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvUsers.CellDoubleClick += dgvUsers_CellDoubleClick;
        dgvUsers.SelectionChanged += dgvUsers_SelectionChanged;

        // panelBottom
        panelBottom.BackColor = Color.FromArgb(240, 240, 240);
        panelBottom.Controls.Add(btnNew);
        panelBottom.Controls.Add(btnEdit);
        panelBottom.Controls.Add(btnDeactivate);
        panelBottom.Controls.Add(btnResetPassword);
        panelBottom.Controls.Add(btnClose);
        panelBottom.Dock = DockStyle.Bottom;
        panelBottom.Height = 50;
        panelBottom.Padding = new Padding(10, 8, 10, 8);

        // btnNew
        btnNew.BackColor = Color.FromArgb(0, 120, 215);
        btnNew.FlatStyle = FlatStyle.Flat;
        btnNew.ForeColor = Color.White;
        btnNew.Location = new Point(10, 10);
        btnNew.Size = new Size(100, 30);
        btnNew.Text = "Novo";
        btnNew.UseVisualStyleBackColor = false;
        btnNew.Click += btnNew_Click;

        // btnEdit
        btnEdit.Location = new Point(120, 10);
        btnEdit.Size = new Size(100, 30);
        btnEdit.Text = "Editar";
        btnEdit.Enabled = false;
        btnEdit.Click += btnEdit_Click;

        // btnDeactivate
        btnDeactivate.Location = new Point(230, 10);
        btnDeactivate.Size = new Size(110, 30);
        btnDeactivate.Text = "Desativar";
        btnDeactivate.Enabled = false;
        btnDeactivate.Click += btnDeactivate_Click;

        // btnResetPassword
        btnResetPassword.Location = new Point(350, 10);
        btnResetPassword.Size = new Size(130, 30);
        btnResetPassword.Text = "Resetar Senha";
        btnResetPassword.Enabled = false;
        btnResetPassword.Click += btnResetPassword_Click;

        // btnClose
        btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnClose.Location = new Point(660, 10);
        btnClose.Size = new Size(100, 30);
        btnClose.Text = "Fechar";
        btnClose.Click += (_, _) => Close();

        // UserManagementForm
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        DoubleBuffered = true;
        ClientSize = new Size(780, 450);
        Controls.Add(dgvUsers);
        Controls.Add(panelBottom);
        Controls.Add(panelTop);
        MinimumSize = new Size(680, 400);
        Name = "UserManagementForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Gerenciamento de Usuários";
        Load += UserManagementForm_Load;

        panelTop.ResumeLayout(false);
        panelTop.PerformLayout();
        panelBottom.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)dgvUsers).EndInit();
        ResumeLayout(false);
    }
}
