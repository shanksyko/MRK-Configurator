#nullable disable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

partial class LoginAutoTab
{
    private IContainer? components = null;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal TextBox txtUserSelector = null!;
    internal TextBox txtPasswordSelector = null!;
    internal TextBox txtSubmitSelector = null!;
    internal TextBox txtPostSubmitSelector = null!;
    internal CheckBox chkUseHeuristics = null!;
    internal CheckBox chkUseJsSetValue = null!;
    internal TextBox txtExtraWaitSelectors = null!;
    internal ListBox lstSsoHints = null!;
    internal TextBox txtSsoHintEntrada = null!;
    internal Button btnAdicionarSsoHint = null!;
    internal Button btnRemoverSsoHint = null!;
    internal ComboBox cmbMfaTipo = null!;
    internal TextBox txtTotpSecretKeyRef = null!;
    internal Button btnDetectarCampos = null!;
    internal Button btnTestarLogin = null!;
    internal Button btnAplicarPosicao = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        components = new Container();
        layoutPrincipal = new TableLayoutPanel();
        var layoutSelectors = new TableLayoutPanel();
        var lblUserSelector = new Label();
        txtUserSelector = new TextBox();
        var lblPasswordSelector = new Label();
        txtPasswordSelector = new TextBox();
        var lblSubmitSelector = new Label();
        txtSubmitSelector = new TextBox();
        var lblPostSubmitSelector = new Label();
        txtPostSubmitSelector = new TextBox();
        var painelToggles = new FlowLayoutPanel();
        chkUseHeuristics = new CheckBox();
        chkUseJsSetValue = new CheckBox();
        var grpExtraWait = new GroupBox();
        txtExtraWaitSelectors = new TextBox();
        var grpSsoHints = new GroupBox();
        var layoutSso = new TableLayoutPanel();
        lstSsoHints = new ListBox();
        var painelSso = new FlowLayoutPanel();
        txtSsoHintEntrada = new TextBox();
        btnAdicionarSsoHint = new Button();
        btnRemoverSsoHint = new Button();
        var layoutMfa = new TableLayoutPanel();
        var lblMfaTipo = new Label();
        cmbMfaTipo = new ComboBox();
        var lblTotpRef = new Label();
        txtTotpSecretKeyRef = new TextBox();
        var painelBotoes = new FlowLayoutPanel();
        btnDetectarCampos = new Button();
        btnTestarLogin = new Button();
        btnAplicarPosicao = new Button();
        SuspendLayout();
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 1;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.Controls.Add(layoutSelectors, 0, 0);
        layoutPrincipal.Controls.Add(painelToggles, 0, 1);
        layoutPrincipal.Controls.Add(grpExtraWait, 0, 2);
        layoutPrincipal.Controls.Add(grpSsoHints, 0, 3);
        layoutPrincipal.Controls.Add(layoutMfa, 0, 4);
        layoutPrincipal.Controls.Add(painelBotoes, 0, 5);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 0);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 6;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.Size = new System.Drawing.Size(640, 520);
        layoutPrincipal.TabIndex = 0;
        //
        // layoutSelectors
        //
        layoutSelectors.ColumnCount = 2;
        layoutSelectors.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutSelectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutSelectors.Controls.Add(lblUserSelector, 0, 0);
        layoutSelectors.Controls.Add(txtUserSelector, 1, 0);
        layoutSelectors.Controls.Add(lblPasswordSelector, 0, 1);
        layoutSelectors.Controls.Add(txtPasswordSelector, 1, 1);
        layoutSelectors.Controls.Add(lblSubmitSelector, 0, 2);
        layoutSelectors.Controls.Add(txtSubmitSelector, 1, 2);
        layoutSelectors.Controls.Add(lblPostSubmitSelector, 0, 3);
        layoutSelectors.Controls.Add(txtPostSubmitSelector, 1, 3);
        layoutSelectors.Dock = DockStyle.Fill;
        layoutSelectors.Margin = new Padding(0, 0, 0, 8);
        layoutSelectors.Name = "layoutSelectors";
        layoutSelectors.RowCount = 4;
        layoutSelectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutSelectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutSelectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutSelectors.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutSelectors.Size = new System.Drawing.Size(624, 128);
        layoutSelectors.TabIndex = 0;
        //
        // lblUserSelector
        //
        lblUserSelector.AutoSize = true;
        lblUserSelector.Margin = new Padding(0, 0, 8, 8);
        lblUserSelector.Name = "lblUserSelector";
        lblUserSelector.Size = new System.Drawing.Size(83, 15);
        lblUserSelector.TabIndex = 0;
        lblUserSelector.Text = "UserSelector";
        //
        // txtUserSelector
        //
        txtUserSelector.Dock = DockStyle.Fill;
        txtUserSelector.Margin = new Padding(0, 0, 0, 8);
        txtUserSelector.Name = "txtUserSelector";
        txtUserSelector.Size = new System.Drawing.Size(524, 23);
        txtUserSelector.TabIndex = 1;
        //
        // lblPasswordSelector
        //
        lblPasswordSelector.AutoSize = true;
        lblPasswordSelector.Margin = new Padding(0, 0, 8, 8);
        lblPasswordSelector.Name = "lblPasswordSelector";
        lblPasswordSelector.Size = new System.Drawing.Size(105, 15);
        lblPasswordSelector.TabIndex = 2;
        lblPasswordSelector.Text = "PasswordSelector";
        //
        // txtPasswordSelector
        //
        txtPasswordSelector.Dock = DockStyle.Fill;
        txtPasswordSelector.Margin = new Padding(0, 0, 0, 8);
        txtPasswordSelector.Name = "txtPasswordSelector";
        txtPasswordSelector.Size = new System.Drawing.Size(524, 23);
        txtPasswordSelector.TabIndex = 3;
        //
        // lblSubmitSelector
        //
        lblSubmitSelector.AutoSize = true;
        lblSubmitSelector.Margin = new Padding(0, 0, 8, 8);
        lblSubmitSelector.Name = "lblSubmitSelector";
        lblSubmitSelector.Size = new System.Drawing.Size(87, 15);
        lblSubmitSelector.TabIndex = 4;
        lblSubmitSelector.Text = "SubmitSelector";
        //
        // txtSubmitSelector
        //
        txtSubmitSelector.Dock = DockStyle.Fill;
        txtSubmitSelector.Margin = new Padding(0, 0, 0, 8);
        txtSubmitSelector.Name = "txtSubmitSelector";
        txtSubmitSelector.Size = new System.Drawing.Size(524, 23);
        txtSubmitSelector.TabIndex = 5;
        //
        // lblPostSubmitSelector
        //
        lblPostSubmitSelector.AutoSize = true;
        lblPostSubmitSelector.Margin = new Padding(0, 0, 8, 0);
        lblPostSubmitSelector.Name = "lblPostSubmitSelector";
        lblPostSubmitSelector.Size = new System.Drawing.Size(111, 15);
        lblPostSubmitSelector.TabIndex = 6;
        lblPostSubmitSelector.Text = "PostSubmitSelector";
        //
        // txtPostSubmitSelector
        //
        txtPostSubmitSelector.Dock = DockStyle.Fill;
        txtPostSubmitSelector.Margin = new Padding(0);
        txtPostSubmitSelector.Name = "txtPostSubmitSelector";
        txtPostSubmitSelector.Size = new System.Drawing.Size(524, 23);
        txtPostSubmitSelector.TabIndex = 7;
        //
        // painelToggles
        //
        painelToggles.AutoSize = true;
        painelToggles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelToggles.Dock = DockStyle.Fill;
        painelToggles.FlowDirection = FlowDirection.LeftToRight;
        painelToggles.Location = new System.Drawing.Point(8, 144);
        painelToggles.Margin = new Padding(0, 8, 0, 8);
        painelToggles.Name = "painelToggles";
        painelToggles.Size = new System.Drawing.Size(624, 27);
        painelToggles.TabIndex = 1;
        painelToggles.WrapContents = false;
        painelToggles.Controls.Add(chkUseHeuristics);
        painelToggles.Controls.Add(chkUseJsSetValue);
        //
        // chkUseHeuristics
        //
        chkUseHeuristics.AutoSize = true;
        chkUseHeuristics.Margin = new Padding(0, 0, 16, 0);
        chkUseHeuristics.Name = "chkUseHeuristics";
        chkUseHeuristics.Size = new System.Drawing.Size(164, 19);
        chkUseHeuristics.TabIndex = 0;
        chkUseHeuristics.Text = "UseHeuristicsFallback";
        chkUseHeuristics.UseVisualStyleBackColor = true;
        //
        // chkUseJsSetValue
        //
        chkUseJsSetValue.AutoSize = true;
        chkUseJsSetValue.Name = "chkUseJsSetValue";
        chkUseJsSetValue.Size = new System.Drawing.Size(120, 19);
        chkUseJsSetValue.TabIndex = 1;
        chkUseJsSetValue.Text = "UseJsSetValue";
        chkUseJsSetValue.UseVisualStyleBackColor = true;
        //
        // grpExtraWait
        //
        grpExtraWait.Controls.Add(txtExtraWaitSelectors);
        grpExtraWait.Dock = DockStyle.Fill;
        grpExtraWait.Location = new System.Drawing.Point(8, 187);
        grpExtraWait.Margin = new Padding(0, 8, 0, 8);
        grpExtraWait.Name = "grpExtraWait";
        grpExtraWait.Padding = new Padding(8);
        grpExtraWait.Size = new System.Drawing.Size(624, 120);
        grpExtraWait.TabIndex = 2;
        grpExtraWait.TabStop = false;
        grpExtraWait.Text = "ExtraWaitSelectors";
        //
        // txtExtraWaitSelectors
        //
        txtExtraWaitSelectors.Dock = DockStyle.Fill;
        txtExtraWaitSelectors.Location = new System.Drawing.Point(8, 24);
        txtExtraWaitSelectors.Margin = new Padding(0);
        txtExtraWaitSelectors.Multiline = true;
        txtExtraWaitSelectors.Name = "txtExtraWaitSelectors";
        txtExtraWaitSelectors.ScrollBars = ScrollBars.Vertical;
        txtExtraWaitSelectors.Size = new System.Drawing.Size(608, 88);
        txtExtraWaitSelectors.TabIndex = 0;
        //
        // grpSsoHints
        //
        grpSsoHints.Controls.Add(layoutSso);
        grpSsoHints.Dock = DockStyle.Fill;
        grpSsoHints.Location = new System.Drawing.Point(8, 315);
        grpSsoHints.Margin = new Padding(0, 0, 0, 8);
        grpSsoHints.Name = "grpSsoHints";
        grpSsoHints.Padding = new Padding(8);
        grpSsoHints.Size = new System.Drawing.Size(624, 120);
        grpSsoHints.TabIndex = 3;
        grpSsoHints.TabStop = false;
        grpSsoHints.Text = "SsoHints";
        //
        // layoutSso
        //
        layoutSso.ColumnCount = 2;
        layoutSso.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        layoutSso.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        layoutSso.Controls.Add(lstSsoHints, 0, 0);
        layoutSso.Controls.Add(painelSso, 1, 0);
        layoutSso.Dock = DockStyle.Fill;
        layoutSso.Location = new System.Drawing.Point(8, 24);
        layoutSso.Margin = new Padding(0);
        layoutSso.Name = "layoutSso";
        layoutSso.RowCount = 1;
        layoutSso.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutSso.Size = new System.Drawing.Size(608, 88);
        layoutSso.TabIndex = 0;
        //
        // lstSsoHints
        //
        lstSsoHints.Dock = DockStyle.Fill;
        lstSsoHints.FormattingEnabled = true;
        lstSsoHints.ItemHeight = 15;
        lstSsoHints.Location = new System.Drawing.Point(3, 3);
        lstSsoHints.Name = "lstSsoHints";
        lstSsoHints.Size = new System.Drawing.Size(359, 82);
        lstSsoHints.TabIndex = 0;
        //
        // painelSso
        //
        painelSso.AutoSize = true;
        painelSso.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelSso.Dock = DockStyle.Fill;
        painelSso.FlowDirection = FlowDirection.TopDown;
        painelSso.Location = new System.Drawing.Point(368, 0);
        painelSso.Margin = new Padding(3, 0, 0, 0);
        painelSso.Name = "painelSso";
        painelSso.Size = new System.Drawing.Size(240, 88);
        painelSso.TabIndex = 1;
        painelSso.Controls.Add(txtSsoHintEntrada);
        painelSso.Controls.Add(btnAdicionarSsoHint);
        painelSso.Controls.Add(btnRemoverSsoHint);
        //
        // txtSsoHintEntrada
        //
        txtSsoHintEntrada.Margin = new Padding(0, 0, 0, 8);
        txtSsoHintEntrada.Name = "txtSsoHintEntrada";
        txtSsoHintEntrada.Size = new System.Drawing.Size(220, 23);
        txtSsoHintEntrada.TabIndex = 0;
        //
        // btnAdicionarSsoHint
        //
        btnAdicionarSsoHint.AutoSize = true;
        btnAdicionarSsoHint.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAdicionarSsoHint.Margin = new Padding(0, 0, 0, 8);
        btnAdicionarSsoHint.Name = "btnAdicionarSsoHint";
        btnAdicionarSsoHint.Size = new System.Drawing.Size(84, 25);
        btnAdicionarSsoHint.TabIndex = 1;
        btnAdicionarSsoHint.Text = "Adicionar";
        btnAdicionarSsoHint.UseVisualStyleBackColor = true;
        btnAdicionarSsoHint.Click += btnAdicionarSsoHint_Click;
        //
        // btnRemoverSsoHint
        //
        btnRemoverSsoHint.AutoSize = true;
        btnRemoverSsoHint.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnRemoverSsoHint.Margin = new Padding(0);
        btnRemoverSsoHint.Name = "btnRemoverSsoHint";
        btnRemoverSsoHint.Size = new System.Drawing.Size(72, 25);
        btnRemoverSsoHint.TabIndex = 2;
        btnRemoverSsoHint.Text = "Remover";
        btnRemoverSsoHint.UseVisualStyleBackColor = true;
        btnRemoverSsoHint.Click += btnRemoverSsoHint_Click;
        //
        // layoutMfa
        //
        layoutMfa.ColumnCount = 4;
        layoutMfa.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutMfa.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        layoutMfa.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutMfa.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        layoutMfa.Controls.Add(lblMfaTipo, 0, 0);
        layoutMfa.Controls.Add(cmbMfaTipo, 1, 0);
        layoutMfa.Controls.Add(lblTotpRef, 2, 0);
        layoutMfa.Controls.Add(txtTotpSecretKeyRef, 3, 0);
        layoutMfa.Dock = DockStyle.Fill;
        layoutMfa.Location = new System.Drawing.Point(8, 443);
        layoutMfa.Margin = new Padding(0, 0, 0, 8);
        layoutMfa.Name = "layoutMfa";
        layoutMfa.RowCount = 1;
        layoutMfa.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutMfa.Size = new System.Drawing.Size(624, 31);
        layoutMfa.TabIndex = 4;
        //
        // lblMfaTipo
        //
        lblMfaTipo.AutoSize = true;
        lblMfaTipo.Margin = new Padding(0, 0, 8, 0);
        lblMfaTipo.Name = "lblMfaTipo";
        lblMfaTipo.Size = new System.Drawing.Size(58, 15);
        lblMfaTipo.TabIndex = 0;
        lblMfaTipo.Text = "MfaTipo";
        //
        // cmbMfaTipo
        //
        cmbMfaTipo.Dock = DockStyle.Fill;
        cmbMfaTipo.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbMfaTipo.Margin = new Padding(0, 0, 16, 0);
        cmbMfaTipo.Name = "cmbMfaTipo";
        cmbMfaTipo.Size = new System.Drawing.Size(181, 23);
        cmbMfaTipo.TabIndex = 1;
        //
        // lblTotpRef
        //
        lblTotpRef.AutoSize = true;
        lblTotpRef.Margin = new Padding(0, 0, 8, 0);
        lblTotpRef.Name = "lblTotpRef";
        lblTotpRef.Size = new System.Drawing.Size(111, 15);
        lblTotpRef.TabIndex = 2;
        lblTotpRef.Text = "TotpSecretKeyRef";
        //
        // txtTotpSecretKeyRef
        //
        txtTotpSecretKeyRef.Dock = DockStyle.Fill;
        txtTotpSecretKeyRef.Margin = new Padding(0);
        txtTotpSecretKeyRef.Name = "txtTotpSecretKeyRef";
        txtTotpSecretKeyRef.ReadOnly = true;
        txtTotpSecretKeyRef.Size = new System.Drawing.Size(262, 23);
        txtTotpSecretKeyRef.TabIndex = 3;
        //
        // painelBotoes
        //
        painelBotoes.AutoSize = true;
        painelBotoes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelBotoes.Dock = DockStyle.Fill;
        painelBotoes.FlowDirection = FlowDirection.LeftToRight;
        painelBotoes.Location = new System.Drawing.Point(8, 482);
        painelBotoes.Margin = new Padding(0);
        painelBotoes.Name = "painelBotoes";
        painelBotoes.Padding = new Padding(0, 8, 0, 0);
        painelBotoes.Size = new System.Drawing.Size(624, 38);
        painelBotoes.TabIndex = 5;
        painelBotoes.WrapContents = false;
        painelBotoes.Controls.Add(btnDetectarCampos);
        painelBotoes.Controls.Add(btnTestarLogin);
        painelBotoes.Controls.Add(btnAplicarPosicao);
        //
        // btnDetectarCampos
        //
        btnDetectarCampos.AutoSize = true;
        btnDetectarCampos.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnDetectarCampos.Margin = new Padding(0, 8, 8, 0);
        btnDetectarCampos.Name = "btnDetectarCampos";
        btnDetectarCampos.Size = new System.Drawing.Size(112, 25);
        btnDetectarCampos.TabIndex = 0;
        btnDetectarCampos.Text = "Detectar Campos";
        btnDetectarCampos.UseVisualStyleBackColor = true;
        btnDetectarCampos.Click += btnDetectarCampos_Click;
        //
        // btnTestarLogin
        //
        btnTestarLogin.AutoSize = true;
        btnTestarLogin.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestarLogin.Margin = new Padding(0, 8, 8, 0);
        btnTestarLogin.Name = "btnTestarLogin";
        btnTestarLogin.Size = new System.Drawing.Size(91, 25);
        btnTestarLogin.TabIndex = 1;
        btnTestarLogin.Text = "Testar Login";
        btnTestarLogin.UseVisualStyleBackColor = true;
        btnTestarLogin.Click += btnTestarLogin_Click;
        //
        // btnAplicarPosicao
        //
        btnAplicarPosicao.AutoSize = true;
        btnAplicarPosicao.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAplicarPosicao.Margin = new Padding(0, 8, 0, 0);
        btnAplicarPosicao.Name = "btnAplicarPosicao";
        btnAplicarPosicao.Size = new System.Drawing.Size(112, 25);
        btnAplicarPosicao.TabIndex = 2;
        btnAplicarPosicao.Text = "Aplicar Posição";
        btnAplicarPosicao.UseVisualStyleBackColor = true;
        btnAplicarPosicao.Click += btnAplicarPosicao_Click;
        //
        // LoginAutoTab
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(layoutPrincipal);
        Name = "LoginAutoTab";
        Size = new System.Drawing.Size(640, 520);
        ResumeLayout(false);
    }

    #endregion
}
