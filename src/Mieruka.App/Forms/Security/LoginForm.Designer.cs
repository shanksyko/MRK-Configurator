#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Security;

partial class LoginForm
{
    private System.ComponentModel.IContainer components = null!;
    private TextBox txtUsername = null!;
    private TextBox txtPassword = null!;
    private Button btnLogin = null!;
    private Button btnCancel = null!;
    private Label lblUsername = null!;
    private Label lblPassword = null!;
    private Label lblTitle = null!;
    private Label lblStatus = null!;
    private Panel panelMain = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.panelMain = new Panel();
        this.lblTitle = new Label();
        this.lblUsername = new Label();
        this.txtUsername = new TextBox();
        this.lblPassword = new Label();
        this.txtPassword = new TextBox();
        this.btnLogin = new Button();
        this.btnCancel = new Button();
        this.lblStatus = new Label();

        this.panelMain.SuspendLayout();
        this.SuspendLayout();

        // panelMain — TableLayoutPanel for responsive layout
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(20),
            BackColor = Color.White,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // title
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // lblUsername
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // txtUsername
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // lblPassword
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // txtPassword
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status

        // lblTitle
        this.lblTitle.Dock = DockStyle.Fill;
        this.lblTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
        this.lblTitle.Name = "lblTitle";
        this.lblTitle.Text = "Mieruka Configurator - Login";
        this.lblTitle.TextAlign = ContentAlignment.MiddleCenter;
        this.lblTitle.AutoSize = true;
        this.lblTitle.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(this.lblTitle, 0, 0);

        // lblUsername
        this.lblUsername.AutoSize = true;
        this.lblUsername.Name = "lblUsername";
        this.lblUsername.Text = "Usuário:";
        this.lblUsername.Margin = new Padding(0, 0, 0, 2);
        layout.Controls.Add(this.lblUsername, 0, 1);

        // txtUsername
        this.txtUsername.Dock = DockStyle.Fill;
        this.txtUsername.Name = "txtUsername";
        this.txtUsername.TabIndex = 0;
        this.txtUsername.Margin = new Padding(0, 0, 0, 8);
        this.txtUsername.KeyPress += new KeyPressEventHandler(this.txtUsername_KeyPress);
        layout.Controls.Add(this.txtUsername, 0, 2);

        // lblPassword
        this.lblPassword.AutoSize = true;
        this.lblPassword.Name = "lblPassword";
        this.lblPassword.Text = "Senha:";
        this.lblPassword.Margin = new Padding(0, 0, 0, 2);
        layout.Controls.Add(this.lblPassword, 0, 3);

        // txtPassword
        this.txtPassword.Dock = DockStyle.Fill;
        this.txtPassword.Name = "txtPassword";
        this.txtPassword.TabIndex = 1;
        this.txtPassword.UseSystemPasswordChar = true;
        this.txtPassword.Margin = new Padding(0, 0, 0, 12);
        this.txtPassword.KeyPress += new KeyPressEventHandler(this.txtPassword_KeyPress);
        layout.Controls.Add(this.txtPassword, 0, 4);

        // Buttons FlowLayoutPanel
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
            WrapContents = false,
        };

        // btnLogin
        this.btnLogin.BackColor = Color.FromArgb(0, 120, 215);
        this.btnLogin.FlatStyle = FlatStyle.Flat;
        this.btnLogin.ForeColor = Color.White;
        this.btnLogin.Name = "btnLogin";
        this.btnLogin.AutoSize = true;
        this.btnLogin.MinimumSize = new Size(120, 30);
        this.btnLogin.TabIndex = 2;
        this.btnLogin.Text = "Entrar";
        this.btnLogin.UseVisualStyleBackColor = false;
        this.btnLogin.Margin = new Padding(0, 0, 8, 0);
        this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click);

        // btnCancel
        this.btnCancel.Name = "btnCancel";
        this.btnCancel.AutoSize = true;
        this.btnCancel.MinimumSize = new Size(120, 30);
        this.btnCancel.TabIndex = 3;
        this.btnCancel.Text = "Cancelar";
        this.btnCancel.UseVisualStyleBackColor = true;
        this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

        buttonPanel.Controls.Add(this.btnLogin);
        buttonPanel.Controls.Add(this.btnCancel);
        layout.Controls.Add(buttonPanel, 0, 5);

        // lblStatus
        this.lblStatus.ForeColor = Color.Red;
        this.lblStatus.Dock = DockStyle.Fill;
        this.lblStatus.Name = "lblStatus";
        this.lblStatus.AutoSize = true;
        this.lblStatus.Text = "";
        this.lblStatus.TextAlign = ContentAlignment.MiddleCenter;
        this.lblStatus.Visible = false;
        layout.Controls.Add(this.lblStatus, 0, 6);

        // panelMain
        this.panelMain.BackColor = Color.White;
        this.panelMain.Controls.Add(layout);
        this.panelMain.Dock = DockStyle.Fill;
        this.panelMain.Name = "panelMain";
        this.panelMain.TabIndex = 0;

        // LoginForm
        this.AcceptButton = this.btnLogin;
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.DoubleBuffered = true;
        this.ClientSize = new Size(400, 260);
        this.MinimumSize = new Size(350, 240);
        this.Controls.Add(this.panelMain);
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Name = "LoginForm";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Text = "Login - Mieruka Configurator";
        this.Load += new System.EventHandler(this.LoginForm_Load);

        this.panelMain.ResumeLayout(false);
        this.panelMain.PerformLayout();
        this.ResumeLayout(false);
    }
}
