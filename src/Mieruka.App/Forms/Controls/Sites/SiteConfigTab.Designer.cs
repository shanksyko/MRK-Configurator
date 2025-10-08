#nullable enable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

partial class SiteConfigTab
{
    private IContainer? components = null;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal TextBox txtUrl = null!;
    internal TextBox txtProfileName = null!;
    internal TextBox txtSessionSelector = null!;
    internal ListBox lstAllowedHosts = null!;
    internal TextBox txtHostEntrada = null!;
    internal Button btnAdicionarHost = null!;
    internal Button btnRemoverHost = null!;
    internal Button btnValidarUrl = null!;
    internal Button btnTestarSessao = null!;
    internal ErrorProvider errorProvider = null!;

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
        var lblUrl = new Label();
        txtUrl = new TextBox();
        var lblProfile = new Label();
        txtProfileName = new TextBox();
        var lblSession = new Label();
        txtSessionSelector = new TextBox();
        var lblHosts = new Label();
        lstAllowedHosts = new ListBox();
        var painelHosts = new FlowLayoutPanel();
        txtHostEntrada = new TextBox();
        btnAdicionarHost = new Button();
        btnRemoverHost = new Button();
        btnValidarUrl = new Button();
        btnTestarSessao = new Button();
        errorProvider = new ErrorProvider(components);
        SuspendLayout();
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 2;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.Controls.Add(lblUrl, 0, 0);
        layoutPrincipal.Controls.Add(txtUrl, 1, 0);
        layoutPrincipal.Controls.Add(lblProfile, 0, 1);
        layoutPrincipal.Controls.Add(txtProfileName, 1, 1);
        layoutPrincipal.Controls.Add(lblSession, 0, 2);
        layoutPrincipal.Controls.Add(txtSessionSelector, 1, 2);
        layoutPrincipal.Controls.Add(lblHosts, 0, 3);
        layoutPrincipal.Controls.Add(lstAllowedHosts, 0, 4);
        layoutPrincipal.SetColumnSpan(lstAllowedHosts, 2);
        layoutPrincipal.Controls.Add(painelHosts, 0, 5);
        layoutPrincipal.SetColumnSpan(painelHosts, 2);
        layoutPrincipal.Controls.Add(btnValidarUrl, 0, 6);
        layoutPrincipal.Controls.Add(btnTestarSessao, 1, 6);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 0);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 7;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.Size = new System.Drawing.Size(600, 500);
        layoutPrincipal.TabIndex = 0;
        //
        // lblUrl
        //
        lblUrl.AutoSize = true;
        lblUrl.Margin = new Padding(0, 0, 8, 8);
        lblUrl.Name = "lblUrl";
        lblUrl.Size = new System.Drawing.Size(26, 15);
        lblUrl.TabIndex = 0;
        lblUrl.Text = "URL";
        //
        // txtUrl
        //
        txtUrl.Dock = DockStyle.Fill;
        txtUrl.Margin = new Padding(0, 0, 0, 8);
        txtUrl.Name = "txtUrl";
        txtUrl.Size = new System.Drawing.Size(568, 23);
        txtUrl.TabIndex = 1;
        //
        // lblProfile
        //
        lblProfile.AutoSize = true;
        lblProfile.Margin = new Padding(0, 0, 8, 8);
        lblProfile.Name = "lblProfile";
        lblProfile.Size = new System.Drawing.Size(77, 15);
        lblProfile.TabIndex = 2;
        lblProfile.Text = "ProfileName";
        //
        // txtProfileName
        //
        txtProfileName.Dock = DockStyle.Fill;
        txtProfileName.Margin = new Padding(0, 0, 0, 8);
        txtProfileName.Name = "txtProfileName";
        txtProfileName.Size = new System.Drawing.Size(568, 23);
        txtProfileName.TabIndex = 3;
        //
        // lblSession
        //
        lblSession.AutoSize = true;
        lblSession.Margin = new Padding(0, 0, 8, 8);
        lblSession.Name = "lblSession";
        lblSession.Size = new System.Drawing.Size(95, 15);
        lblSession.TabIndex = 4;
        lblSession.Text = "SessionSelector";
        //
        // txtSessionSelector
        //
        txtSessionSelector.Dock = DockStyle.Fill;
        txtSessionSelector.Margin = new Padding(0, 0, 0, 8);
        txtSessionSelector.Name = "txtSessionSelector";
        txtSessionSelector.Size = new System.Drawing.Size(568, 23);
        txtSessionSelector.TabIndex = 5;
        //
        // lblHosts
        //
        lblHosts.AutoSize = true;
        lblHosts.Margin = new Padding(0, 0, 0, 8);
        lblHosts.Name = "lblHosts";
        lblHosts.Size = new System.Drawing.Size(115, 15);
        lblHosts.TabIndex = 6;
        lblHosts.Text = "AllowedTabHosts";
        //
        // lstAllowedHosts
        //
        lstAllowedHosts.Dock = DockStyle.Fill;
        lstAllowedHosts.FormattingEnabled = true;
        lstAllowedHosts.ItemHeight = 15;
        lstAllowedHosts.Location = new System.Drawing.Point(11, 152);
        lstAllowedHosts.Margin = new Padding(3, 0, 3, 8);
        lstAllowedHosts.Name = "lstAllowedHosts";
        lstAllowedHosts.Size = new System.Drawing.Size(576, 276);
        lstAllowedHosts.TabIndex = 7;
        //
        // painelHosts
        //
        painelHosts.AutoSize = true;
        painelHosts.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelHosts.Dock = DockStyle.Fill;
        painelHosts.FlowDirection = FlowDirection.LeftToRight;
        painelHosts.Location = new System.Drawing.Point(11, 436);
        painelHosts.Margin = new Padding(3, 0, 3, 8);
        painelHosts.Name = "painelHosts";
        painelHosts.Size = new System.Drawing.Size(576, 33);
        painelHosts.TabIndex = 8;
        painelHosts.WrapContents = false;
        painelHosts.Controls.Add(txtHostEntrada);
        painelHosts.Controls.Add(btnAdicionarHost);
        painelHosts.Controls.Add(btnRemoverHost);
        //
        // txtHostEntrada
        //
        txtHostEntrada.Margin = new Padding(0, 0, 8, 0);
        txtHostEntrada.Name = "txtHostEntrada";
        txtHostEntrada.Size = new System.Drawing.Size(260, 23);
        txtHostEntrada.TabIndex = 0;
        //
        // btnAdicionarHost
        //
        btnAdicionarHost.AutoSize = true;
        btnAdicionarHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAdicionarHost.Margin = new Padding(0, 0, 8, 0);
        btnAdicionarHost.Name = "btnAdicionarHost";
        btnAdicionarHost.Size = new System.Drawing.Size(84, 25);
        btnAdicionarHost.TabIndex = 1;
        btnAdicionarHost.Text = "Adicionar";
        btnAdicionarHost.UseVisualStyleBackColor = true;
        btnAdicionarHost.Click += btnAdicionarHost_Click;
        //
        // btnRemoverHost
        //
        btnRemoverHost.AutoSize = true;
        btnRemoverHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnRemoverHost.Margin = new Padding(0);
        btnRemoverHost.Name = "btnRemoverHost";
        btnRemoverHost.Size = new System.Drawing.Size(72, 25);
        btnRemoverHost.TabIndex = 2;
        btnRemoverHost.Text = "Remover";
        btnRemoverHost.UseVisualStyleBackColor = true;
        btnRemoverHost.Click += btnRemoverHost_Click;
        //
        // btnValidarUrl
        //
        btnValidarUrl.AutoSize = true;
        btnValidarUrl.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnValidarUrl.Margin = new Padding(0, 0, 8, 0);
        btnValidarUrl.Name = "btnValidarUrl";
        btnValidarUrl.Size = new System.Drawing.Size(86, 25);
        btnValidarUrl.TabIndex = 9;
        btnValidarUrl.Text = "Validar URL";
        btnValidarUrl.UseVisualStyleBackColor = true;
        btnValidarUrl.Click += btnValidarUrl_Click;
        //
        // btnTestarSessao
        //
        btnTestarSessao.AutoSize = true;
        btnTestarSessao.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestarSessao.Margin = new Padding(0);
        btnTestarSessao.Name = "btnTestarSessao";
        btnTestarSessao.Size = new System.Drawing.Size(94, 25);
        btnTestarSessao.TabIndex = 10;
        btnTestarSessao.Text = "Testar Sessão";
        btnTestarSessao.UseVisualStyleBackColor = true;
        btnTestarSessao.Click += btnTestarSessao_Click;
        //
        // errorProvider
        //
        errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        errorProvider.ContainerControl = this;
        //
        // SiteConfigTab
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(layoutPrincipal);
        Name = "SiteConfigTab";
        Size = new System.Drawing.Size(600, 500);
        ResumeLayout(false);
    }

    #endregion
}
