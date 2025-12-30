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

        // panelMain
        this.panelMain.BackColor = Color.White;
        this.panelMain.Controls.Add(this.lblTitle);
        this.panelMain.Controls.Add(this.lblUsername);
        this.panelMain.Controls.Add(this.txtUsername);
        this.panelMain.Controls.Add(this.lblPassword);
        this.panelMain.Controls.Add(this.txtPassword);
        this.panelMain.Controls.Add(this.btnLogin);
        this.panelMain.Controls.Add(this.btnCancel);
        this.panelMain.Controls.Add(this.lblStatus);
        this.panelMain.Dock = DockStyle.Fill;
        this.panelMain.Location = new Point(0, 0);
        this.panelMain.Name = "panelMain";
        this.panelMain.Padding = new Padding(20);
        this.panelMain.Size = new Size(400, 250);
        this.panelMain.TabIndex = 0;

        // lblTitle
        this.lblTitle.AutoSize = false;
        this.lblTitle.Font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Point);
        this.lblTitle.Location = new Point(20, 20);
        this.lblTitle.Name = "lblTitle";
        this.lblTitle.Size = new Size(360, 30);
        this.lblTitle.TabIndex = 0;
        this.lblTitle.Text = "Mieruka Configurator - Login";
        this.lblTitle.TextAlign = ContentAlignment.MiddleCenter;

        // lblUsername
        this.lblUsername.AutoSize = true;
        this.lblUsername.Location = new Point(20, 70);
        this.lblUsername.Name = "lblUsername";
        this.lblUsername.Size = new Size(53, 15);
        this.lblUsername.TabIndex = 1;
        this.lblUsername.Text = "Usuário:";

        // txtUsername
        this.txtUsername.Location = new Point(20, 90);
        this.txtUsername.Name = "txtUsername";
        this.txtUsername.Size = new Size(360, 23);
        this.txtUsername.TabIndex = 0;
        this.txtUsername.KeyPress += new KeyPressEventHandler(this.txtUsername_KeyPress);

        // lblPassword
        this.lblPassword.AutoSize = true;
        this.lblPassword.Location = new Point(20, 120);
        this.lblPassword.Name = "lblPassword";
        this.lblPassword.Size = new Size(42, 15);
        this.lblPassword.TabIndex = 3;
        this.lblPassword.Text = "Senha:";

        // txtPassword
        this.txtPassword.Location = new Point(20, 140);
        this.txtPassword.Name = "txtPassword";
        this.txtPassword.Size = new Size(360, 23);
        this.txtPassword.TabIndex = 1;
        this.txtPassword.UseSystemPasswordChar = true;
        this.txtPassword.KeyPress += new KeyPressEventHandler(this.txtPassword_KeyPress);

        // btnLogin
        this.btnLogin.BackColor = Color.FromArgb(0, 120, 215);
        this.btnLogin.FlatStyle = FlatStyle.Flat;
        this.btnLogin.ForeColor = Color.White;
        this.btnLogin.Location = new Point(120, 180);
        this.btnLogin.Name = "btnLogin";
        this.btnLogin.Size = new Size(120, 30);
        this.btnLogin.TabIndex = 2;
        this.btnLogin.Text = "Entrar";
        this.btnLogin.UseVisualStyleBackColor = false;
        this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click);

        // btnCancel
        this.btnCancel.Location = new Point(260, 180);
        this.btnCancel.Name = "btnCancel";
        this.btnCancel.Size = new Size(120, 30);
        this.btnCancel.TabIndex = 3;
        this.btnCancel.Text = "Cancelar";
        this.btnCancel.UseVisualStyleBackColor = true;
        this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);

        // lblStatus
        this.lblStatus.ForeColor = Color.Red;
        this.lblStatus.Location = new Point(20, 215);
        this.lblStatus.Name = "lblStatus";
        this.lblStatus.Size = new Size(360, 15);
        this.lblStatus.TabIndex = 7;
        this.lblStatus.Text = "";
        this.lblStatus.TextAlign = ContentAlignment.MiddleCenter;
        this.lblStatus.Visible = false;

        // LoginForm
        this.AcceptButton = this.btnLogin;
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(400, 250);
        this.Controls.Add(this.panelMain);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
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
