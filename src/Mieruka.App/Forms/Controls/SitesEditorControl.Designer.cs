#nullable disable
#nullable enable annotations
using System.ComponentModel;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls.Sites;

namespace Mieruka.App.Forms.Controls;

partial class SitesEditorControl
{
    private IContainer? components = null;
    internal SplitContainer splitContainer = null!;
    internal DataGridView dgvSites = null!;
    internal BindingSource bsSites = null!;
    internal Button btnAdicionarSite = null!;
    internal Button btnRemoverSite = null!;
    internal Button btnClonarSite = null!;
    internal Button btnTestarSite = null!;
    internal Button btnAplicarPosicao = null!;
    internal SiteConfigTab siteConfigTab = null!;
    internal LoginAutoTab loginAutoTab = null!;
    internal WhitelistTab whitelistTab = null!;
    internal ArgsTab argsTab = null!;
    internal CredentialVaultPanel credentialVaultPanel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private void InitializeComponent()
    {
        components = new Container();
        splitContainer = new SplitContainer();
        var groupSites = new GroupBox();
        var tableSites = new TableLayoutPanel();
        dgvSites = new DataGridView();
        bsSites = new BindingSource(components);
        var buttonPanel = new FlowLayoutPanel();
        btnAdicionarSite = new Button();
        btnRemoverSite = new Button();
        btnClonarSite = new Button();
        btnTestarSite = new Button();
        btnAplicarPosicao = new Button();
        var tabDetalhes = new TabControl();
        var tpConfig = new TabPage();
        var tpLogin = new TabPage();
        var tpWhitelist = new TabPage();
        var tpArgs = new TabPage();
        var tpVault = new TabPage();
        siteConfigTab = new SiteConfigTab();
        loginAutoTab = new LoginAutoTab();
        whitelistTab = new WhitelistTab();
        argsTab = new ArgsTab();
        credentialVaultPanel = new CredentialVaultPanel();
        ((ISupportInitialize)splitContainer).BeginInit();
        splitContainer.Panel1.SuspendLayout();
        splitContainer.Panel2.SuspendLayout();
        splitContainer.SuspendLayout();
        groupSites.SuspendLayout();
        tableSites.SuspendLayout();
        ((ISupportInitialize)dgvSites).BeginInit();
        ((ISupportInitialize)bsSites).BeginInit();
        buttonPanel.SuspendLayout();
        tabDetalhes.SuspendLayout();
        tpConfig.SuspendLayout();
        tpLogin.SuspendLayout();
        tpWhitelist.SuspendLayout();
        tpArgs.SuspendLayout();
        tpVault.SuspendLayout();
        SuspendLayout();
        // 
        // splitContainer
        // 
        splitContainer.Dock = DockStyle.Fill;
        splitContainer.Location = new System.Drawing.Point(0, 0);
        splitContainer.Name = "splitContainer";
        splitContainer.Orientation = Orientation.Vertical;
        // 
        // splitContainer.Panel1
        // 
        splitContainer.Panel1.Controls.Add(groupSites);
        splitContainer.Panel1MinSize = 220;
        // 
        // splitContainer.Panel2
        // 
        splitContainer.Panel2.Controls.Add(tabDetalhes);
        splitContainer.Panel2MinSize = 320;
        splitContainer.Size = new System.Drawing.Size(1000, 580);
        splitContainer.SplitterDistance = 360;
        splitContainer.TabIndex = 0;
        // 
        // groupSites
        // 
        groupSites.Controls.Add(tableSites);
        groupSites.Dock = DockStyle.Fill;
        groupSites.Location = new System.Drawing.Point(0, 0);
        groupSites.Name = "groupSites";
        groupSites.Padding = new Padding(8);
        groupSites.Size = new System.Drawing.Size(360, 580);
        groupSites.TabIndex = 0;
        groupSites.TabStop = false;
        groupSites.Text = "Sites do Programa";
        // 
        // tableSites
        // 
        tableSites.ColumnCount = 1;
        tableSites.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tableSites.Controls.Add(dgvSites, 0, 1);
        tableSites.Controls.Add(buttonPanel, 0, 0);
        tableSites.Dock = DockStyle.Fill;
        tableSites.Location = new System.Drawing.Point(8, 24);
        tableSites.Name = "tableSites";
        tableSites.RowCount = 2;
        tableSites.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tableSites.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tableSites.Size = new System.Drawing.Size(344, 548);
        tableSites.TabIndex = 0;
        // 
        // dgvSites
        // 
        dgvSites.AllowUserToAddRows = false;
        dgvSites.AllowUserToDeleteRows = false;
        dgvSites.AutoGenerateColumns = false;
        dgvSites.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvSites.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Mieruka.Core.Models.SiteConfig.Id),
                HeaderText = "SiteId",
                Width = 140,
                ReadOnly = true,
                Name = "colSiteId"
            },
            new DataGridViewTextBoxColumn
            {
                DataPropertyName = nameof(Mieruka.Core.Models.SiteConfig.Url),
                HeaderText = "URL",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true,
                Name = "colUrl"
            }
        });
        dgvSites.DataSource = bsSites;
        dgvSites.Dock = DockStyle.Fill;
        dgvSites.MultiSelect = false;
        dgvSites.ReadOnly = true;
        dgvSites.RowHeadersVisible = false;
        dgvSites.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvSites.TabIndex = 1;
        dgvSites.SelectionChanged += dgvSites_SelectionChanged;
        // 
        // buttonPanel
        // 
        buttonPanel.AutoSize = true;
        buttonPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.FlowDirection = FlowDirection.LeftToRight;
        buttonPanel.Padding = new Padding(0, 0, 0, 6);
        buttonPanel.WrapContents = false;
        buttonPanel.Controls.Add(btnAdicionarSite);
        buttonPanel.Controls.Add(btnRemoverSite);
        buttonPanel.Controls.Add(btnClonarSite);
        buttonPanel.Controls.Add(btnTestarSite);
        buttonPanel.Controls.Add(btnAplicarPosicao);
        // 
        // btnAdicionarSite
        // 
        btnAdicionarSite.AutoSize = true;
        btnAdicionarSite.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAdicionarSite.Margin = new Padding(0, 0, 6, 0);
        btnAdicionarSite.Name = "btnAdicionarSite";
        btnAdicionarSite.Size = new System.Drawing.Size(73, 25);
        btnAdicionarSite.TabIndex = 0;
        btnAdicionarSite.Text = "Adicionar";
        btnAdicionarSite.UseVisualStyleBackColor = true;
        btnAdicionarSite.Click += btnAdicionarSite_Click;
        // 
        // btnRemoverSite
        // 
        btnRemoverSite.AutoSize = true;
        btnRemoverSite.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnRemoverSite.Margin = new Padding(0, 0, 6, 0);
        btnRemoverSite.Name = "btnRemoverSite";
        btnRemoverSite.Size = new System.Drawing.Size(64, 25);
        btnRemoverSite.TabIndex = 1;
        btnRemoverSite.Text = "Remover";
        btnRemoverSite.UseVisualStyleBackColor = true;
        btnRemoverSite.Click += btnRemoverSite_Click;
        // 
        // btnClonarSite
        // 
        btnClonarSite.AutoSize = true;
        btnClonarSite.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnClonarSite.Margin = new Padding(0, 0, 6, 0);
        btnClonarSite.Name = "btnClonarSite";
        btnClonarSite.Size = new System.Drawing.Size(52, 25);
        btnClonarSite.TabIndex = 2;
        btnClonarSite.Text = "Clonar";
        btnClonarSite.UseVisualStyleBackColor = true;
        btnClonarSite.Click += btnClonarSite_Click;
        // 
        // btnTestarSite
        // 
        btnTestarSite.AutoSize = true;
        btnTestarSite.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestarSite.Margin = new Padding(0, 0, 6, 0);
        btnTestarSite.Name = "btnTestarSite";
        btnTestarSite.Size = new System.Drawing.Size(92, 25);
        btnTestarSite.TabIndex = 3;
        btnTestarSite.Text = "Testar Login";
        btnTestarSite.UseVisualStyleBackColor = true;
        btnTestarSite.Click += btnTestarSite_Click;
        // 
        // btnAplicarPosicao
        // 
        btnAplicarPosicao.AutoSize = true;
        btnAplicarPosicao.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAplicarPosicao.Margin = new Padding(0);
        btnAplicarPosicao.Name = "btnAplicarPosicao";
        btnAplicarPosicao.Size = new System.Drawing.Size(111, 25);
        btnAplicarPosicao.TabIndex = 4;
        btnAplicarPosicao.Text = "Aplicar Posição";
        btnAplicarPosicao.UseVisualStyleBackColor = true;
        btnAplicarPosicao.Click += btnAplicarPosicao_Click;
        // 
        // tabDetalhes
        // 
        tabDetalhes.Controls.Add(tpConfig);
        tabDetalhes.Controls.Add(tpLogin);
        tabDetalhes.Controls.Add(tpWhitelist);
        tabDetalhes.Controls.Add(tpArgs);
        tabDetalhes.Controls.Add(tpVault);
        tabDetalhes.Dock = DockStyle.Fill;
        tabDetalhes.Location = new System.Drawing.Point(0, 0);
        tabDetalhes.Name = "tabDetalhes";
        tabDetalhes.SelectedIndex = 0;
        tabDetalhes.Size = new System.Drawing.Size(636, 580);
        tabDetalhes.TabIndex = 0;
        // 
        // tpConfig
        // 
        tpConfig.Controls.Add(siteConfigTab);
        tpConfig.Location = new System.Drawing.Point(4, 24);
        tpConfig.Name = "tpConfig";
        tpConfig.Padding = new Padding(8);
        tpConfig.Size = new System.Drawing.Size(628, 552);
        tpConfig.TabIndex = 0;
        tpConfig.Text = "Config";
        tpConfig.UseVisualStyleBackColor = true;
        // 
        // siteConfigTab
        // 
        siteConfigTab.Dock = DockStyle.Fill;
        siteConfigTab.Location = new System.Drawing.Point(8, 8);
        siteConfigTab.Name = "siteConfigTab";
        siteConfigTab.Size = new System.Drawing.Size(612, 536);
        siteConfigTab.TabIndex = 0;
        // 
        // tpLogin
        // 
        tpLogin.Controls.Add(loginAutoTab);
        tpLogin.Location = new System.Drawing.Point(4, 24);
        tpLogin.Name = "tpLogin";
        tpLogin.Padding = new Padding(8);
        tpLogin.Size = new System.Drawing.Size(628, 552);
        tpLogin.TabIndex = 1;
        tpLogin.Text = "Login Automático";
        tpLogin.UseVisualStyleBackColor = true;
        // 
        // loginAutoTab
        // 
        loginAutoTab.Dock = DockStyle.Fill;
        loginAutoTab.Location = new System.Drawing.Point(8, 8);
        loginAutoTab.Name = "loginAutoTab";
        loginAutoTab.Size = new System.Drawing.Size(612, 536);
        loginAutoTab.TabIndex = 0;
        // 
        // tpWhitelist
        // 
        tpWhitelist.Controls.Add(whitelistTab);
        tpWhitelist.Location = new System.Drawing.Point(4, 24);
        tpWhitelist.Name = "tpWhitelist";
        tpWhitelist.Padding = new Padding(8);
        tpWhitelist.Size = new System.Drawing.Size(628, 552);
        tpWhitelist.TabIndex = 2;
        tpWhitelist.Text = "Whitelist";
        tpWhitelist.UseVisualStyleBackColor = true;
        // 
        // whitelistTab
        // 
        whitelistTab.Dock = DockStyle.Fill;
        whitelistTab.Location = new System.Drawing.Point(8, 8);
        whitelistTab.Name = "whitelistTab";
        whitelistTab.Size = new System.Drawing.Size(612, 536);
        whitelistTab.TabIndex = 0;
        // 
        // tpArgs
        // 
        tpArgs.Controls.Add(argsTab);
        tpArgs.Location = new System.Drawing.Point(4, 24);
        tpArgs.Name = "tpArgs";
        tpArgs.Padding = new Padding(8);
        tpArgs.Size = new System.Drawing.Size(628, 552);
        tpArgs.TabIndex = 3;
        tpArgs.Text = "Args";
        tpArgs.UseVisualStyleBackColor = true;
        // 
        // argsTab
        // 
        argsTab.Dock = DockStyle.Fill;
        argsTab.Location = new System.Drawing.Point(8, 8);
        argsTab.Name = "argsTab";
        argsTab.Size = new System.Drawing.Size(612, 536);
        argsTab.TabIndex = 0;
        // 
        // tpVault
        // 
        tpVault.Controls.Add(credentialVaultPanel);
        tpVault.Location = new System.Drawing.Point(4, 24);
        tpVault.Name = "tpVault";
        tpVault.Padding = new Padding(8);
        tpVault.Size = new System.Drawing.Size(628, 552);
        tpVault.TabIndex = 4;
        tpVault.Text = "CredentialVault";
        tpVault.UseVisualStyleBackColor = true;
        // 
        // credentialVaultPanel
        // 
        credentialVaultPanel.Dock = DockStyle.Fill;
        credentialVaultPanel.Location = new System.Drawing.Point(8, 8);
        credentialVaultPanel.Name = "credentialVaultPanel";
        credentialVaultPanel.Size = new System.Drawing.Size(612, 536);
        credentialVaultPanel.TabIndex = 0;
        // 
        // SitesEditorControl
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(splitContainer);
        Name = "SitesEditorControl";
        Size = new System.Drawing.Size(1000, 580);
        splitContainer.Panel1.ResumeLayout(false);
        splitContainer.Panel2.ResumeLayout(false);
        ((ISupportInitialize)splitContainer).EndInit();
        splitContainer.ResumeLayout(false);
        groupSites.ResumeLayout(false);
        tableSites.ResumeLayout(false);
        tableSites.PerformLayout();
        ((ISupportInitialize)dgvSites).EndInit();
        ((ISupportInitialize)bsSites).EndInit();
        buttonPanel.ResumeLayout(false);
        buttonPanel.PerformLayout();
        tabDetalhes.ResumeLayout(false);
        tpConfig.ResumeLayout(false);
        tpLogin.ResumeLayout(false);
        tpWhitelist.ResumeLayout(false);
        tpArgs.ResumeLayout(false);
        tpVault.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion
}
