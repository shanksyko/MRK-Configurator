#nullable enable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls;

partial class CredentialVaultPanel
{
    private IContainer? components = null;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal Label lblScope = null!;
    internal Label lblStatus = null!;
    internal TextBox txtUsuario = null!;
    internal TextBox txtSenha = null!;
    internal TextBox txtTotp = null!;
    internal Button btnSalvar = null!;
    internal Button btnApagar = null!;
    internal Button btnTestar = null!;

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new Container();
        layoutPrincipal = new TableLayoutPanel();
        lblScope = new Label();
        lblStatus = new Label();
        txtUsuario = new TextBox();
        txtSenha = new TextBox();
        txtTotp = new TextBox();
        var painelBotoes = new FlowLayoutPanel();
        btnSalvar = new Button();
        btnApagar = new Button();
        btnTestar = new Button();
        SuspendLayout();
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 1;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.Controls.Add(lblScope, 0, 0);
        layoutPrincipal.Controls.Add(lblStatus, 0, 1);
        layoutPrincipal.Controls.Add(txtUsuario, 0, 2);
        layoutPrincipal.Controls.Add(txtSenha, 0, 3);
        layoutPrincipal.Controls.Add(txtTotp, 0, 4);
        layoutPrincipal.Controls.Add(painelBotoes, 0, 5);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 0);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 6;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.Size = new System.Drawing.Size(400, 280);
        layoutPrincipal.TabIndex = 0;
        //
        // lblScope
        //
        lblScope.AutoSize = true;
        lblScope.Margin = new Padding(0, 0, 0, 8);
        lblScope.Name = "lblScope";
        lblScope.Size = new System.Drawing.Size(108, 15);
        lblScope.TabIndex = 0;
        lblScope.Text = "Selecione um site";
        //
        // lblStatus
        //
        lblStatus.AutoSize = true;
        lblStatus.Margin = new Padding(0, 0, 0, 8);
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new System.Drawing.Size(0, 15);
        lblStatus.TabIndex = 1;
        //
        // txtUsuario
        //
        txtUsuario.Dock = DockStyle.Fill;
        txtUsuario.Margin = new Padding(0, 0, 0, 8);
        txtUsuario.Name = "txtUsuario";
        txtUsuario.PlaceholderText = "Usuário";
        txtUsuario.Size = new System.Drawing.Size(384, 23);
        txtUsuario.TabIndex = 2;
        txtUsuario.UseSystemPasswordChar = true;
        //
        // txtSenha
        //
        txtSenha.Dock = DockStyle.Fill;
        txtSenha.Margin = new Padding(0, 0, 0, 8);
        txtSenha.Name = "txtSenha";
        txtSenha.PlaceholderText = "Senha";
        txtSenha.Size = new System.Drawing.Size(384, 23);
        txtSenha.TabIndex = 3;
        txtSenha.UseSystemPasswordChar = true;
        //
        // txtTotp
        //
        txtTotp.Dock = DockStyle.Fill;
        txtTotp.Margin = new Padding(0, 0, 0, 8);
        txtTotp.Name = "txtTotp";
        txtTotp.PlaceholderText = "TOTP";
        txtTotp.Size = new System.Drawing.Size(384, 23);
        txtTotp.TabIndex = 4;
        txtTotp.UseSystemPasswordChar = true;
        //
        // painelBotoes
        //
        painelBotoes.AutoSize = true;
        painelBotoes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelBotoes.Dock = DockStyle.Fill;
        painelBotoes.FlowDirection = FlowDirection.LeftToRight;
        painelBotoes.Location = new System.Drawing.Point(11, 219);
        painelBotoes.Margin = new Padding(3, 0, 3, 0);
        painelBotoes.Name = "painelBotoes";
        painelBotoes.Size = new System.Drawing.Size(378, 46);
        painelBotoes.TabIndex = 5;
        painelBotoes.WrapContents = false;
        painelBotoes.Controls.Add(btnSalvar);
        painelBotoes.Controls.Add(btnApagar);
        painelBotoes.Controls.Add(btnTestar);
        //
        // btnSalvar
        //
        btnSalvar.AutoSize = true;
        btnSalvar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnSalvar.Margin = new Padding(0, 0, 8, 0);
        btnSalvar.Name = "btnSalvar";
        btnSalvar.Size = new System.Drawing.Size(84, 25);
        btnSalvar.TabIndex = 0;
        btnSalvar.Text = "Salvar";
        btnSalvar.UseVisualStyleBackColor = true;
        btnSalvar.Click += btnSalvar_Click;
        //
        // btnApagar
        //
        btnApagar.AutoSize = true;
        btnApagar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnApagar.Margin = new Padding(0, 0, 8, 0);
        btnApagar.Name = "btnApagar";
        btnApagar.Size = new System.Drawing.Size(70, 25);
        btnApagar.TabIndex = 1;
        btnApagar.Text = "Apagar";
        btnApagar.UseVisualStyleBackColor = true;
        btnApagar.Click += btnApagar_Click;
        //
        // btnTestar
        //
        btnTestar.AutoSize = true;
        btnTestar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestar.Margin = new Padding(0);
        btnTestar.Name = "btnTestar";
        btnTestar.Size = new System.Drawing.Size(93, 25);
        btnTestar.TabIndex = 2;
        btnTestar.Text = "Testar Login";
        btnTestar.UseVisualStyleBackColor = true;
        btnTestar.Click += btnTestar_Click;
        //
        // CredentialVaultPanel
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(layoutPrincipal);
        Name = "CredentialVaultPanel";
        Size = new System.Drawing.Size(400, 280);
        ResumeLayout(false);
    }

    #endregion
}
