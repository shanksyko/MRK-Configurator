#nullable disable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms;

partial class MainForm
{
    private IContainer? components = null;
    internal MenuStrip menuPrincipal = null!;
    internal ToolStripMenuItem menuPerfis = null!;
    internal ToolStripMenuItem menuPerfisSalvar = null!;
    internal ToolStripMenuItem menuPerfisExecutar = null!;
    internal ToolStripMenuItem menuPerfisParar = null!;
    internal ToolStripMenuItem menuPerfisTestar = null!;
    internal ToolStripSeparator menuPerfisSeparador = null!;
    internal ToolStripMenuItem menuPerfisCarregar = null!;
    internal ToolStripMenuItem menuExibir = null!;
    internal ToolStripMenuItem menuPreview = null!;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal GroupBox grpMonitores = null!;
    internal TableLayoutPanel tlpMonitores = null!;
    internal DataGridView dgvProgramas = null!;
    internal BindingSource bsProgramas = null!;
    internal FlowLayoutPanel painelBotoes = null!;
    internal Button btnAdicionar = null!;
    internal Button btnEditar = null!;
    internal Button btnDuplicar = null!;
    internal Button btnExcluir = null!;
    internal Button btnExecutar = null!;
    internal Button btnParar = null!;
    internal Button btnSalvarPerfil = null!;
    internal Button btnExecutarPerfil = null!;
    internal Button btnPararPerfil = null!;
    internal Button btnTestarItem = null!;
    internal ErrorProvider errorProvider = null!;
    internal StatusStrip statusBar = null!;
    internal ToolStripStatusLabel lblStatus = null!;

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
        menuPrincipal = new MenuStrip();
        menuPerfis = new ToolStripMenuItem();
        menuPerfisSalvar = new ToolStripMenuItem();
        menuPerfisExecutar = new ToolStripMenuItem();
        menuPerfisParar = new ToolStripMenuItem();
        menuPerfisTestar = new ToolStripMenuItem();
        menuPerfisSeparador = new ToolStripSeparator();
        menuPerfisCarregar = new ToolStripMenuItem();
        menuExibir = new ToolStripMenuItem();
        menuPreview = new ToolStripMenuItem();
        layoutPrincipal = new TableLayoutPanel();
        grpMonitores = new GroupBox();
        tlpMonitores = new TableLayoutPanel();
        dgvProgramas = new DataGridView();
        bsProgramas = new BindingSource(components);
        painelBotoes = new FlowLayoutPanel();
        btnAdicionar = new Button();
        btnEditar = new Button();
        btnDuplicar = new Button();
        btnExcluir = new Button();
        btnExecutar = new Button();
        btnParar = new Button();
        btnSalvarPerfil = new Button();
        btnExecutarPerfil = new Button();
        btnPararPerfil = new Button();
        btnTestarItem = new Button();
        errorProvider = new ErrorProvider(components);
        statusBar = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();
        var colId = new DataGridViewTextBoxColumn();
        var colExecutavel = new DataGridViewTextBoxColumn();
        var colAutoStart = new DataGridViewCheckBoxColumn();
        menuPrincipal.SuspendLayout();
        layoutPrincipal.SuspendLayout();
        grpMonitores.SuspendLayout();
        tlpMonitores.SuspendLayout();
        ((ISupportInitialize)dgvProgramas).BeginInit();
        ((ISupportInitialize)bsProgramas).BeginInit();
        painelBotoes.SuspendLayout();
        ((ISupportInitialize)errorProvider).BeginInit();
        statusBar.SuspendLayout();
        SuspendLayout();
        //
        // menuPrincipal
        //
        menuPrincipal.ImageScalingSize = new System.Drawing.Size(24, 24);
        menuPrincipal.Items.AddRange(new ToolStripItem[] { menuPerfis, menuExibir });
        menuPrincipal.Location = new System.Drawing.Point(0, 0);
        menuPrincipal.Name = "menuPrincipal";
        menuPrincipal.Padding = new Padding(8, 3, 0, 3);
        menuPrincipal.Size = new System.Drawing.Size(1180, 30);
        menuPrincipal.TabIndex = 0;
        menuPrincipal.Text = "menuStrip1";
        //
        // menuPerfis
        //
        menuPerfis.DropDownItems.AddRange(new ToolStripItem[] { menuPerfisSalvar, menuPerfisExecutar, menuPerfisParar, menuPerfisTestar, menuPerfisSeparador, menuPerfisCarregar });
        menuPerfis.Name = "menuPerfis";
        menuPerfis.Size = new System.Drawing.Size(61, 24);
        menuPerfis.Text = "&Perfis";
        //
        // menuPerfisSalvar
        //
        menuPerfisSalvar.Name = "menuPerfisSalvar";
        menuPerfisSalvar.Size = new System.Drawing.Size(246, 26);
        menuPerfisSalvar.Text = "Salvar perfil";
        menuPerfisSalvar.Click += menuPerfisSalvar_Click;
        //
        // menuPerfisExecutar
        //
        menuPerfisExecutar.Name = "menuPerfisExecutar";
        menuPerfisExecutar.Size = new System.Drawing.Size(246, 26);
        menuPerfisExecutar.Text = "Executar perfil";
        menuPerfisExecutar.Click += menuPerfisExecutar_Click;
        //
        // menuPerfisParar
        //
        menuPerfisParar.Name = "menuPerfisParar";
        menuPerfisParar.Size = new System.Drawing.Size(246, 26);
        menuPerfisParar.Text = "Parar";
        menuPerfisParar.Click += menuPerfisParar_Click;
        //
        // menuPerfisTestar
        //
        menuPerfisTestar.Name = "menuPerfisTestar";
        menuPerfisTestar.Size = new System.Drawing.Size(246, 26);
        menuPerfisTestar.Text = "Testar item";
        menuPerfisTestar.Click += menuPerfisTestar_Click;
        //
        // menuPerfisSeparador
        //
        menuPerfisSeparador.Name = "menuPerfisSeparador";
        menuPerfisSeparador.Size = new System.Drawing.Size(243, 6);
        //
        // menuPerfisCarregar
        //
        menuPerfisCarregar.Name = "menuPerfisCarregar";
        menuPerfisCarregar.Size = new System.Drawing.Size(246, 26);
        menuPerfisCarregar.Text = "Carregar perfil...";
        menuPerfisCarregar.Click += menuPerfisCarregar_Click;
        //
        // menuExibir
        //
        menuExibir.DropDownItems.AddRange(new ToolStripItem[] { menuPreview });
        menuExibir.Name = "menuExibir";
        menuExibir.Size = new System.Drawing.Size(58, 24);
        menuExibir.Text = "&Exibir";
        //
        // menuPreview
        //
        menuPreview.Name = "menuPreview";
        menuPreview.Size = new System.Drawing.Size(180, 24);
        menuPreview.Text = "Preview...";
        menuPreview.Click += menuPreview_Click;
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 2;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutPrincipal.Controls.Add(dgvProgramas, 0, 0);
        layoutPrincipal.Controls.Add(painelBotoes, 1, 0);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 350);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 1;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutPrincipal.Size = new System.Drawing.Size(1180, 350);
        layoutPrincipal.TabIndex = 1;
        //
        // grpMonitores
        //
        grpMonitores.Controls.Add(tlpMonitores);
        grpMonitores.Dock = DockStyle.Top;
        grpMonitores.Location = new System.Drawing.Point(0, 30);
        grpMonitores.Margin = new Padding(8);
        grpMonitores.Name = "grpMonitores";
        grpMonitores.Padding = new Padding(8);
        grpMonitores.Size = new System.Drawing.Size(1180, 320);
        grpMonitores.TabIndex = 2;
        grpMonitores.TabStop = false;
        grpMonitores.Text = "Monitores";
        //
        // tlpMonitores
        //
        tlpMonitores.AutoScroll = true;
        tlpMonitores.AutoSize = true;
        tlpMonitores.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        tlpMonitores.ColumnCount = 2;
        tlpMonitores.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        tlpMonitores.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        tlpMonitores.Dock = DockStyle.Fill;
        tlpMonitores.Location = new System.Drawing.Point(8, 24);
        tlpMonitores.Margin = new Padding(0);
        tlpMonitores.Name = "tlpMonitores";
        tlpMonitores.Padding = new Padding(0, 0, 0, 8);
        tlpMonitores.RowCount = 1;
        tlpMonitores.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpMonitores.Size = new System.Drawing.Size(1164, 288);
        tlpMonitores.TabIndex = 0;
        tlpMonitores.SizeChanged += tlpMonitores_SizeChanged;
        //
        // dgvProgramas
        //
        dgvProgramas.AllowUserToAddRows = false;
        dgvProgramas.AllowUserToDeleteRows = false;
        dgvProgramas.AutoGenerateColumns = false;
        dgvProgramas.BackgroundColor = System.Drawing.SystemColors.Window;
        dgvProgramas.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        dgvProgramas.Columns.AddRange(new DataGridViewColumn[] { colId, colExecutavel, colAutoStart });
        dgvProgramas.DataSource = bsProgramas;
        dgvProgramas.Dock = DockStyle.Fill;
        dgvProgramas.Location = new System.Drawing.Point(11, 11);
        dgvProgramas.Margin = new Padding(3, 3, 8, 3);
        dgvProgramas.MultiSelect = false;
        dgvProgramas.Name = "dgvProgramas";
        dgvProgramas.ReadOnly = true;
        dgvProgramas.RowHeadersVisible = false;
        dgvProgramas.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvProgramas.Size = new System.Drawing.Size(1009, 648);
        dgvProgramas.TabIndex = 0;
        dgvProgramas.CellDoubleClick += dgvProgramas_CellDoubleClick;
        dgvProgramas.SelectionChanged += dgvProgramas_SelectionChanged;
        dgvProgramas.KeyDown += dgvProgramas_KeyDown;
        //
        // colId
        //
        colId.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        colId.DataPropertyName = nameof(Mieruka.Core.Models.AppConfig.Id);
        colId.HeaderText = "ID";
        colId.MinimumWidth = 120;
        colId.Name = "colId";
        colId.ReadOnly = true;
        //
        // colExecutavel
        //
        colExecutavel.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        colExecutavel.DataPropertyName = nameof(Mieruka.Core.Models.AppConfig.ExecutablePath);
        colExecutavel.HeaderText = "Executável";
        colExecutavel.MinimumWidth = 200;
        colExecutavel.Name = "colExecutavel";
        colExecutavel.ReadOnly = true;
        //
        // colAutoStart
        //
        colAutoStart.AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        colAutoStart.DataPropertyName = nameof(Mieruka.Core.Models.AppConfig.AutoStart);
        colAutoStart.HeaderText = "AutoStart";
        colAutoStart.MinimumWidth = 80;
        colAutoStart.Name = "colAutoStart";
        colAutoStart.ReadOnly = true;
        //
        // painelBotoes
        //
        painelBotoes.AutoSize = true;
        painelBotoes.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelBotoes.Controls.Add(btnAdicionar);
        painelBotoes.Controls.Add(btnEditar);
        painelBotoes.Controls.Add(btnDuplicar);
        painelBotoes.Controls.Add(btnExcluir);
        painelBotoes.Controls.Add(btnExecutar);
        painelBotoes.Controls.Add(btnParar);
        painelBotoes.Controls.Add(btnSalvarPerfil);
        painelBotoes.Controls.Add(btnExecutarPerfil);
        painelBotoes.Controls.Add(btnPararPerfil);
        painelBotoes.Controls.Add(btnTestarItem);
        painelBotoes.Dock = DockStyle.Fill;
        painelBotoes.FlowDirection = FlowDirection.TopDown;
        painelBotoes.Location = new System.Drawing.Point(1032, 11);
        painelBotoes.Margin = new Padding(0);
        painelBotoes.Name = "painelBotoes";
        painelBotoes.Padding = new Padding(0, 0, 0, 8);
        painelBotoes.Size = new System.Drawing.Size(140, 648);
        painelBotoes.TabIndex = 1;
        painelBotoes.WrapContents = false;
        //
        // btnAdicionar
        //
        btnAdicionar.AutoSize = true;
        btnAdicionar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAdicionar.Margin = new Padding(0, 0, 0, 8);
        btnAdicionar.Name = "btnAdicionar";
        btnAdicionar.Size = new System.Drawing.Size(84, 25);
        btnAdicionar.TabIndex = 0;
        btnAdicionar.Text = "Adicionar";
        btnAdicionar.UseVisualStyleBackColor = true;
        btnAdicionar.Click += btnAdicionar_Click;
        //
        // btnEditar
        //
        btnEditar.AutoSize = true;
        btnEditar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnEditar.Margin = new Padding(0, 0, 0, 8);
        btnEditar.Name = "btnEditar";
        btnEditar.Size = new System.Drawing.Size(61, 25);
        btnEditar.TabIndex = 1;
        btnEditar.Text = "Editar";
        btnEditar.UseVisualStyleBackColor = true;
        btnEditar.Click += btnEditar_Click;
        //
        // btnDuplicar
        //
        btnDuplicar.AutoSize = true;
        btnDuplicar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnDuplicar.Margin = new Padding(0, 0, 0, 8);
        btnDuplicar.Name = "btnDuplicar";
        btnDuplicar.Size = new System.Drawing.Size(75, 25);
        btnDuplicar.TabIndex = 2;
        btnDuplicar.Text = "Duplicar";
        btnDuplicar.UseVisualStyleBackColor = true;
        btnDuplicar.Click += btnDuplicar_Click;
        //
        // btnExcluir
        //
        btnExcluir.AutoSize = true;
        btnExcluir.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnExcluir.Margin = new Padding(0, 0, 0, 8);
        btnExcluir.Name = "btnExcluir";
        btnExcluir.Size = new System.Drawing.Size(63, 25);
        btnExcluir.TabIndex = 3;
        btnExcluir.Text = "Excluir";
        btnExcluir.UseVisualStyleBackColor = true;
        btnExcluir.Click += btnExcluir_Click;
        //
        // btnExecutar
        //
        btnExecutar.AutoSize = true;
        btnExecutar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnExecutar.Margin = new Padding(0, 0, 0, 8);
        btnExecutar.Name = "btnExecutar";
        btnExecutar.Size = new System.Drawing.Size(70, 25);
        btnExecutar.TabIndex = 4;
        btnExecutar.Text = "Executar";
        btnExecutar.UseVisualStyleBackColor = true;
        btnExecutar.Click += btnExecutar_Click;
        //
        // btnParar
        //
        btnParar.AutoSize = true;
        btnParar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnParar.Margin = new Padding(0, 0, 0, 8);
        btnParar.Name = "btnParar";
        btnParar.Size = new System.Drawing.Size(51, 25);
        btnParar.TabIndex = 5;
        btnParar.Text = "Parar";
        btnParar.UseVisualStyleBackColor = true;
        btnParar.Click += btnParar_Click;
        //
        // btnSalvarPerfil
        //
        btnSalvarPerfil.AutoSize = true;
        btnSalvarPerfil.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnSalvarPerfil.Margin = new Padding(0, 0, 0, 8);
        btnSalvarPerfil.Name = "btnSalvarPerfil";
        btnSalvarPerfil.Size = new System.Drawing.Size(94, 25);
        btnSalvarPerfil.TabIndex = 6;
        btnSalvarPerfil.Text = "Salvar perfil";
        btnSalvarPerfil.UseVisualStyleBackColor = true;
        btnSalvarPerfil.Click += btnSalvarPerfil_Click;
        //
        // btnExecutarPerfil
        //
        btnExecutarPerfil.AutoSize = true;
        btnExecutarPerfil.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnExecutarPerfil.Margin = new Padding(0, 0, 0, 8);
        btnExecutarPerfil.Name = "btnExecutarPerfil";
        btnExecutarPerfil.Size = new System.Drawing.Size(108, 25);
        btnExecutarPerfil.TabIndex = 7;
        btnExecutarPerfil.Text = "Executar perfil";
        btnExecutarPerfil.UseVisualStyleBackColor = true;
        btnExecutarPerfil.Click += btnExecutarPerfil_Click;
        //
        // btnPararPerfil
        //
        btnPararPerfil.AutoSize = true;
        btnPararPerfil.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnPararPerfil.Margin = new Padding(0, 0, 0, 8);
        btnPararPerfil.Name = "btnPararPerfil";
        btnPararPerfil.Size = new System.Drawing.Size(85, 25);
        btnPararPerfil.TabIndex = 8;
        btnPararPerfil.Text = "Parar perfil";
        btnPararPerfil.UseVisualStyleBackColor = true;
        btnPararPerfil.Click += btnPararPerfil_Click;
        //
        // btnTestarItem
        //
        btnTestarItem.AutoSize = true;
        btnTestarItem.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestarItem.Margin = new Padding(0);
        btnTestarItem.Name = "btnTestarItem";
        btnTestarItem.Size = new System.Drawing.Size(85, 25);
        btnTestarItem.TabIndex = 9;
        btnTestarItem.Text = "Testar item";
        btnTestarItem.UseVisualStyleBackColor = true;
        btnTestarItem.Click += btnTestarItem_Click;
        //
        // errorProvider
        //
        errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        errorProvider.ContainerControl = this;
        //
        // statusBar
        //
        statusBar.ImageScalingSize = new System.Drawing.Size(24, 24);
        statusBar.Items.AddRange(new ToolStripItem[] { lblStatus });
        statusBar.Location = new System.Drawing.Point(0, 678);
        statusBar.Name = "statusBar";
        statusBar.Size = new System.Drawing.Size(1180, 22);
        statusBar.TabIndex = 3;
        statusBar.Text = "statusBar";
        //
        // lblStatus
        //
        lblStatus.Name = "lblStatus";
        lblStatus.Size = new System.Drawing.Size(54, 17);
        lblStatus.Text = "Pronto";
        //
        // MainForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1180, 700);
        Controls.Add(statusBar);
        Controls.Add(layoutPrincipal);
        Controls.Add(grpMonitores);
        Controls.Add(menuPrincipal);
        MainMenuStrip = menuPrincipal;
        MinimumSize = new System.Drawing.Size(960, 640);
        Name = "MainForm";
        Padding = new Padding(0);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Mieruka Configurator";
        menuPrincipal.ResumeLayout(false);
        menuPrincipal.PerformLayout();
        layoutPrincipal.ResumeLayout(false);
        layoutPrincipal.PerformLayout();
        tlpMonitores.ResumeLayout(false);
        tlpMonitores.PerformLayout();
        grpMonitores.ResumeLayout(false);
        grpMonitores.PerformLayout();
        ((ISupportInitialize)dgvProgramas).EndInit();
        ((ISupportInitialize)bsProgramas).EndInit();
        painelBotoes.ResumeLayout(false);
        painelBotoes.PerformLayout();
        ((ISupportInitialize)errorProvider).EndInit();
        statusBar.ResumeLayout(false);
        statusBar.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
