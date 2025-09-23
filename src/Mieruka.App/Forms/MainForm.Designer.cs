#nullable disable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms;

partial class MainForm
{
    private IContainer? components = null;
    internal MenuStrip menuPrincipal = null!;
    internal ToolStripMenuItem menuExibir = null!;
    internal ToolStripMenuItem menuPreview = null!;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal DataGridView dgvProgramas = null!;
    internal BindingSource bsProgramas = null!;
    internal FlowLayoutPanel painelBotoes = null!;
    internal Button btnAdicionar = null!;
    internal Button btnEditar = null!;
    internal Button btnDuplicar = null!;
    internal Button btnExcluir = null!;
    internal Button btnExecutar = null!;
    internal Button btnParar = null!;
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
        menuPrincipal = new MenuStrip();
        menuExibir = new ToolStripMenuItem();
        menuPreview = new ToolStripMenuItem();
        layoutPrincipal = new TableLayoutPanel();
        dgvProgramas = new DataGridView();
        bsProgramas = new BindingSource(components);
        painelBotoes = new FlowLayoutPanel();
        btnAdicionar = new Button();
        btnEditar = new Button();
        btnDuplicar = new Button();
        btnExcluir = new Button();
        btnExecutar = new Button();
        btnParar = new Button();
        errorProvider = new ErrorProvider(components);
        var colId = new DataGridViewTextBoxColumn();
        var colExecutavel = new DataGridViewTextBoxColumn();
        var colAutoStart = new DataGridViewCheckBoxColumn();
        menuPrincipal.SuspendLayout();
        layoutPrincipal.SuspendLayout();
        ((ISupportInitialize)dgvProgramas).BeginInit();
        ((ISupportInitialize)bsProgramas).BeginInit();
        painelBotoes.SuspendLayout();
        ((ISupportInitialize)errorProvider).BeginInit();
        SuspendLayout();
        //
        // menuPrincipal
        //
        menuPrincipal.ImageScalingSize = new System.Drawing.Size(24, 24);
        menuPrincipal.Items.AddRange(new ToolStripItem[] { menuExibir });
        menuPrincipal.Location = new System.Drawing.Point(0, 0);
        menuPrincipal.Name = "menuPrincipal";
        menuPrincipal.Padding = new Padding(8, 3, 0, 3);
        menuPrincipal.Size = new System.Drawing.Size(1180, 30);
        menuPrincipal.TabIndex = 0;
        menuPrincipal.Text = "menuStrip1";
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
        layoutPrincipal.Location = new System.Drawing.Point(0, 30);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 1;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutPrincipal.Size = new System.Drawing.Size(1180, 670);
        layoutPrincipal.TabIndex = 1;
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
        btnParar.Margin = new Padding(0);
        btnParar.Name = "btnParar";
        btnParar.Size = new System.Drawing.Size(51, 25);
        btnParar.TabIndex = 5;
        btnParar.Text = "Parar";
        btnParar.UseVisualStyleBackColor = true;
        btnParar.Click += btnParar_Click;
        //
        // errorProvider
        //
        errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        errorProvider.ContainerControl = this;
        //
        // MainForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1180, 700);
        Controls.Add(layoutPrincipal);
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
        ((ISupportInitialize)dgvProgramas).EndInit();
        ((ISupportInitialize)bsProgramas).EndInit();
        painelBotoes.ResumeLayout(false);
        painelBotoes.PerformLayout();
        ((ISupportInitialize)errorProvider).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
