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

        // panelTop — use TableLayoutPanel for responsive search bar
        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(0),
        };
        topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // title
        topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // search row
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // lblSearch
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60f)); // txtSearch
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); // lblRole
        topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f)); // cmbRole

        // lblTitle
        lblTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        lblTitle.Text = "Gerenciamento de Usuários";
        lblTitle.AutoSize = true;
        lblTitle.Margin = new Padding(0, 0, 0, 6);
        topLayout.Controls.Add(lblTitle, 0, 0);
        topLayout.SetColumnSpan(lblTitle, 4);

        // lblSearch
        lblSearch.AutoSize = true;
        lblSearch.Text = "Buscar:";
        lblSearch.Anchor = AnchorStyles.Left;
        lblSearch.Margin = new Padding(0, 0, 4, 0);
        topLayout.Controls.Add(lblSearch, 0, 1);

        // txtSearch
        txtSearch.Dock = DockStyle.Fill;
        txtSearch.Margin = new Padding(0, 0, 12, 0);
        txtSearch.TextChanged += txtSearch_TextChanged;
        topLayout.Controls.Add(txtSearch, 1, 1);

        // lblRoleFilter
        lblRoleFilter.AutoSize = true;
        lblRoleFilter.Text = "Perfil:";
        lblRoleFilter.Anchor = AnchorStyles.Left;
        lblRoleFilter.Margin = new Padding(0, 0, 4, 0);
        topLayout.Controls.Add(lblRoleFilter, 2, 1);

        // cmbRoleFilter
        cmbRoleFilter.Dock = DockStyle.Fill;
        cmbRoleFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRoleFilter.SelectedIndexChanged += cmbRoleFilter_SelectedIndexChanged;
        topLayout.Controls.Add(cmbRoleFilter, 3, 1);

        panelTop.BackColor = Color.FromArgb(240, 240, 240);
        panelTop.Controls.Add(topLayout);
        panelTop.Dock = DockStyle.Top;
        panelTop.Height = 80;
        panelTop.Padding = new Padding(10, 8, 10, 8);

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

        // panelBottom — use FlowLayoutPanel for responsive buttons
        var bottomFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = false,
            WrapContents = false,
            Padding = new Padding(0),
        };

        // btnNew
        btnNew.BackColor = Color.FromArgb(0, 120, 215);
        btnNew.FlatStyle = FlatStyle.Flat;
        btnNew.ForeColor = Color.White;
        btnNew.AutoSize = true;
        btnNew.MinimumSize = new Size(90, 30);
        btnNew.Text = "Novo";
        btnNew.UseVisualStyleBackColor = false;
        btnNew.Margin = new Padding(0, 0, 6, 0);
        btnNew.Click += btnNew_Click;

        // btnEdit
        btnEdit.AutoSize = true;
        btnEdit.MinimumSize = new Size(90, 30);
        btnEdit.Text = "Editar";
        btnEdit.Enabled = false;
        btnEdit.Margin = new Padding(0, 0, 6, 0);
        btnEdit.Click += btnEdit_Click;

        // btnDeactivate
        btnDeactivate.AutoSize = true;
        btnDeactivate.MinimumSize = new Size(100, 30);
        btnDeactivate.Text = "Desativar";
        btnDeactivate.Enabled = false;
        btnDeactivate.Margin = new Padding(0, 0, 6, 0);
        btnDeactivate.Click += btnDeactivate_Click;

        // btnResetPassword
        btnResetPassword.AutoSize = true;
        btnResetPassword.MinimumSize = new Size(120, 30);
        btnResetPassword.Text = "Resetar Senha";
        btnResetPassword.Enabled = false;
        btnResetPassword.Margin = new Padding(0, 0, 6, 0);
        btnResetPassword.Click += btnResetPassword_Click;

        // btnClose
        btnClose.AutoSize = true;
        btnClose.MinimumSize = new Size(90, 30);
        btnClose.Text = "Fechar";
        btnClose.Click += (_, _) => Close();

        bottomFlow.Controls.Add(btnNew);
        bottomFlow.Controls.Add(btnEdit);
        bottomFlow.Controls.Add(btnDeactivate);
        bottomFlow.Controls.Add(btnResetPassword);
        bottomFlow.Controls.Add(btnClose);

        panelBottom.BackColor = Color.FromArgb(240, 240, 240);
        panelBottom.Controls.Add(bottomFlow);
        panelBottom.Dock = DockStyle.Bottom;
        panelBottom.Height = 50;
        panelBottom.Padding = new Padding(10, 8, 10, 8);

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
