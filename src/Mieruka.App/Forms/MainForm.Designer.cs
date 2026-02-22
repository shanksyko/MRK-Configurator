#nullable enable
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

    internal ToolStripMenuItem menuSeguranca = null!;
    internal ToolStripMenuItem menuSegurancaUsuarios = null!;
    internal ToolStripMenuItem menuSegurancaCredenciais = null!;
    internal ToolStripSeparator menuSegurancaSeparador = null!;
    internal ToolStripMenuItem menuSegurancaAuditoria = null!;

    internal ToolStripMenuItem menuConfiguracao = null!;
    internal ToolStripMenuItem menuConfiguracaoExportar = null!;
    internal ToolStripMenuItem menuConfiguracaoImportar = null!;
    internal ToolStripMenuItem menuConfiguracaoBackup = null!;
    internal ToolStripMenuItem menuConfiguracaoRestaurar = null!;
    internal ToolStripMenuItem menuConfiguracaoRetencao = null!;
    internal ToolStripMenuItem menuConfiguracaoHistorico = null!;
    internal ToolStripMenuItem menuConfiguracaoDashboard = null!;
    internal ToolStripMenuItem menuConfiguracaoAgendamento = null!;
    internal ToolStripMenuItem menuConfiguracaoModoEscuro = null!;
    internal ToolStripMenuItem menuConfiguracaoIdioma = null!;
    internal ToolStripMenuItem menuConfiguracaoIdiomaPtBr = null!;
    internal ToolStripMenuItem menuConfiguracaoIdiomaEnUs = null!;

    internal TableLayoutPanel layoutPrincipal = null!;
    internal GroupBox grpMonitores = null!;
    internal TableLayoutPanel tlpMonitores = null!;
    internal DataGridView dgvProgramas = null!;
    internal BindingSource bsProgramas = null!;
    internal FlowLayoutPanel painelBotoes = null!;
    internal Controls.ModernButton btnAdicionar = null!;
    internal Controls.ModernButton btnEditar = null!;
    internal Controls.ModernButton btnDuplicar = null!;
    internal Controls.ModernButton btnExcluir = null!;
    internal Controls.ModernButton btnExecutar = null!;
    internal Controls.ModernButton btnParar = null!;
    internal Controls.ModernButton btnSalvarPerfil = null!;
    internal Controls.ModernButton btnExecutarPerfil = null!;
    internal Controls.ModernButton btnPararPerfil = null!;
    internal Controls.ModernButton btnTestarItem = null!;
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

        menuSeguranca = new ToolStripMenuItem();
        menuSegurancaUsuarios = new ToolStripMenuItem();
        menuSegurancaCredenciais = new ToolStripMenuItem();
        menuSegurancaSeparador = new ToolStripSeparator();
        menuSegurancaAuditoria = new ToolStripMenuItem();

        menuConfiguracao = new ToolStripMenuItem();
        menuConfiguracaoExportar = new ToolStripMenuItem();
        menuConfiguracaoImportar = new ToolStripMenuItem();
        menuConfiguracaoBackup = new ToolStripMenuItem();
        menuConfiguracaoRestaurar = new ToolStripMenuItem();
        menuConfiguracaoRetencao = new ToolStripMenuItem();
        menuConfiguracaoHistorico = new ToolStripMenuItem();
        menuConfiguracaoDashboard = new ToolStripMenuItem();
        menuConfiguracaoAgendamento = new ToolStripMenuItem();
        menuConfiguracaoModoEscuro = new ToolStripMenuItem();
        menuConfiguracaoIdioma = new ToolStripMenuItem();
        menuConfiguracaoIdiomaPtBr = new ToolStripMenuItem();
        menuConfiguracaoIdiomaEnUs = new ToolStripMenuItem();
        var menuConfiguracaoSep1 = new ToolStripSeparator();
        var menuConfiguracaoSep2 = new ToolStripSeparator();
        var menuConfiguracaoSep3 = new ToolStripSeparator();
        var menuConfiguracaoSep4 = new ToolStripSeparator();

        layoutPrincipal = new TableLayoutPanel();
        grpMonitores = new GroupBox();
        tlpMonitores = new TableLayoutPanel();
        dgvProgramas = new DataGridView();
        bsProgramas = new BindingSource(components);
        painelBotoes = new FlowLayoutPanel();
        btnAdicionar = new Controls.ModernButton();
        btnEditar = new Controls.ModernButton();
        btnDuplicar = new Controls.ModernButton();
        btnExcluir = new Controls.ModernButton();
        btnExecutar = new Controls.ModernButton();
        btnParar = new Controls.ModernButton();
        btnSalvarPerfil = new Controls.ModernButton();
        btnExecutarPerfil = new Controls.ModernButton();
        btnPararPerfil = new Controls.ModernButton();
        btnTestarItem = new Controls.ModernButton();
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
        menuPrincipal.Items.AddRange(new ToolStripItem[] { menuPerfis, menuConfiguracao, menuSeguranca });
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
        // menuSeguranca
        //
        menuSeguranca.DropDownItems.AddRange(new ToolStripItem[] { menuSegurancaUsuarios, menuSegurancaCredenciais, menuSegurancaSeparador, menuSegurancaAuditoria });
        menuSeguranca.Name = "menuSeguranca";
        menuSeguranca.Text = "&Segurança";
        //
        // menuSegurancaUsuarios
        //
        menuSegurancaUsuarios.Name = "menuSegurancaUsuarios";
        menuSegurancaUsuarios.Text = "Gerenciar Usuários";
        menuSegurancaUsuarios.Click += menuSegurancaUsuarios_Click;
        //
        // menuSegurancaCredenciais
        //
        menuSegurancaCredenciais.Name = "menuSegurancaCredenciais";
        menuSegurancaCredenciais.Text = "Gerenciar Credenciais";
        menuSegurancaCredenciais.Click += menuSegurancaCredenciais_Click;
        //
        // menuSegurancaSeparador
        //
        menuSegurancaSeparador.Name = "menuSegurancaSeparador";
        //
        // menuSegurancaAuditoria
        //
        menuSegurancaAuditoria.Name = "menuSegurancaAuditoria";
        menuSegurancaAuditoria.Text = "Log de Auditoria";
        menuSegurancaAuditoria.Click += menuSegurancaAuditoria_Click;
        //
        // menuConfiguracao
        //
        menuConfiguracao.DropDownItems.AddRange(new ToolStripItem[] {
            menuConfiguracaoExportar, menuConfiguracaoImportar,
            menuConfiguracaoSep1,
            menuConfiguracaoBackup, menuConfiguracaoRestaurar,
            menuConfiguracaoSep2,
            menuConfiguracaoRetencao, menuConfiguracaoHistorico,
            menuConfiguracaoSep3,
            menuConfiguracaoDashboard, menuConfiguracaoAgendamento,
            menuConfiguracaoSep4,
            menuConfiguracaoModoEscuro, menuConfiguracaoIdioma
        });
        menuConfiguracao.Name = "menuConfiguracao";
        menuConfiguracao.Text = "&Configuração";
        //
        // menuConfiguracaoExportar
        //
        menuConfiguracaoExportar.Name = "menuConfiguracaoExportar";
        menuConfiguracaoExportar.Text = "Exportar Configuração...";
        menuConfiguracaoExportar.Click += menuConfiguracaoExportar_Click;
        //
        // menuConfiguracaoImportar
        //
        menuConfiguracaoImportar.Name = "menuConfiguracaoImportar";
        menuConfiguracaoImportar.Text = "Importar Configuração...";
        menuConfiguracaoImportar.Click += menuConfiguracaoImportar_Click;
        //
        // menuConfiguracaoBackup
        //
        menuConfiguracaoBackup.Name = "menuConfiguracaoBackup";
        menuConfiguracaoBackup.Text = "Backup do Banco...";
        menuConfiguracaoBackup.Click += menuConfiguracaoBackup_Click;
        //
        // menuConfiguracaoRestaurar
        //
        menuConfiguracaoRestaurar.Name = "menuConfiguracaoRestaurar";
        menuConfiguracaoRestaurar.Text = "Restaurar Banco...";
        menuConfiguracaoRestaurar.Click += menuConfiguracaoRestaurar_Click;
        //
        // menuConfiguracaoRetencao
        //
        menuConfiguracaoRetencao.Name = "menuConfiguracaoRetencao";
        menuConfiguracaoRetencao.Text = "Retenção de Dados...";
        menuConfiguracaoRetencao.Click += menuConfiguracaoRetencao_Click;
        //
        // menuConfiguracaoHistorico
        //
        menuConfiguracaoHistorico.Name = "menuConfiguracaoHistorico";
        menuConfiguracaoHistorico.Text = "Histórico de Configuração...";
        menuConfiguracaoHistorico.Click += menuConfiguracaoHistorico_Click;
        //
        // menuConfiguracaoDashboard
        //
        menuConfiguracaoDashboard.Name = "menuConfiguracaoDashboard";
        menuConfiguracaoDashboard.Text = "Dashboard de Status";
        menuConfiguracaoDashboard.Click += menuConfiguracaoDashboard_Click;
        //
        // menuConfiguracaoAgendamento
        //
        menuConfiguracaoAgendamento.Name = "menuConfiguracaoAgendamento";
        menuConfiguracaoAgendamento.Text = "Agendamento...";
        menuConfiguracaoAgendamento.Click += menuConfiguracaoAgendamento_Click;
        //
        // menuConfiguracaoModoEscuro
        //
        menuConfiguracaoModoEscuro.Name = "menuConfiguracaoModoEscuro";
        menuConfiguracaoModoEscuro.Text = "Modo Escuro";
        menuConfiguracaoModoEscuro.CheckOnClick = true;
        menuConfiguracaoModoEscuro.Click += menuConfiguracaoModoEscuro_Click;
        //
        // menuConfiguracaoIdioma
        //
        menuConfiguracaoIdioma.DropDownItems.AddRange(new ToolStripItem[] { menuConfiguracaoIdiomaPtBr, menuConfiguracaoIdiomaEnUs });
        menuConfiguracaoIdioma.Name = "menuConfiguracaoIdioma";
        menuConfiguracaoIdioma.Text = "Idioma";
        //
        // menuConfiguracaoIdiomaPtBr
        //
        menuConfiguracaoIdiomaPtBr.Name = "menuConfiguracaoIdiomaPtBr";
        menuConfiguracaoIdiomaPtBr.Text = "Português";
        menuConfiguracaoIdiomaPtBr.Checked = true;
        menuConfiguracaoIdiomaPtBr.Click += menuConfiguracaoIdiomaPtBr_Click;
        //
        // menuConfiguracaoIdiomaEnUs
        //
        menuConfiguracaoIdiomaEnUs.Name = "menuConfiguracaoIdiomaEnUs";
        menuConfiguracaoIdiomaEnUs.Text = "English";
        menuConfiguracaoIdiomaEnUs.Click += menuConfiguracaoIdiomaEnUs_Click;
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
        dgvProgramas.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI Semibold", 9F);
        dgvProgramas.ColumnHeadersDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(240, 240, 240);
        dgvProgramas.EnableHeadersVisualStyles = false;
        dgvProgramas.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(245, 248, 255);
        dgvProgramas.DefaultCellStyle.SelectionBackColor = System.Drawing.Color.FromArgb(0, 120, 215);
        dgvProgramas.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.White;
        dgvProgramas.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        dgvProgramas.RowTemplate.Height = 28;
        dgvProgramas.BorderStyle = BorderStyle.None;
        dgvProgramas.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgvProgramas.GridColor = System.Drawing.Color.FromArgb(230, 230, 230);
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
        var sep1 = new Label { AutoSize = false, Height = 1, Dock = DockStyle.Top, BorderStyle = BorderStyle.Fixed3D, Margin = new Padding(0, 4, 0, 4) };
        var sep2 = new Label { AutoSize = false, Height = 1, Dock = DockStyle.Top, BorderStyle = BorderStyle.Fixed3D, Margin = new Padding(0, 4, 0, 4) };
        painelBotoes.Controls.Add(btnAdicionar);
        painelBotoes.Controls.Add(btnEditar);
        painelBotoes.Controls.Add(btnDuplicar);
        painelBotoes.Controls.Add(btnExcluir);
        painelBotoes.Controls.Add(sep1);
        painelBotoes.Controls.Add(btnExecutar);
        painelBotoes.Controls.Add(btnParar);
        painelBotoes.Controls.Add(btnTestarItem);
        painelBotoes.Controls.Add(sep2);
        painelBotoes.Controls.Add(btnSalvarPerfil);
        painelBotoes.Controls.Add(btnExecutarPerfil);
        painelBotoes.Controls.Add(btnPararPerfil);
        painelBotoes.Dock = DockStyle.Fill;
        painelBotoes.FlowDirection = FlowDirection.TopDown;
        painelBotoes.Location = new System.Drawing.Point(1032, 11);
        painelBotoes.Margin = new Padding(0);
        painelBotoes.Name = "painelBotoes";
        painelBotoes.Padding = new Padding(4, 0, 0, 8);
        painelBotoes.Size = new System.Drawing.Size(150, 648);
        painelBotoes.TabIndex = 1;
        painelBotoes.WrapContents = false;
        //
        // btnAdicionar
        //
        btnAdicionar.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);
        btnAdicionar.ForeColor = System.Drawing.Color.White;
        btnAdicionar.Margin = new Padding(0, 0, 0, 6);
        btnAdicionar.Name = "btnAdicionar";
        btnAdicionar.Size = new System.Drawing.Size(130, 30);
        btnAdicionar.TabIndex = 0;
        btnAdicionar.Text = "\u2795  Adicionar";
        btnAdicionar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnAdicionar.Padding = new Padding(8, 0, 0, 0);
        btnAdicionar.Click += btnAdicionar_Click;
        //
        // btnEditar
        //
        btnEditar.Margin = new Padding(0, 0, 0, 6);
        btnEditar.Name = "btnEditar";
        btnEditar.Size = new System.Drawing.Size(130, 30);
        btnEditar.TabIndex = 1;
        btnEditar.Text = "\u270F\uFE0F  Editar";
        btnEditar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnEditar.Padding = new Padding(8, 0, 0, 0);
        btnEditar.Click += btnEditar_Click;
        //
        // btnDuplicar
        //
        btnDuplicar.Margin = new Padding(0, 0, 0, 6);
        btnDuplicar.Name = "btnDuplicar";
        btnDuplicar.Size = new System.Drawing.Size(130, 30);
        btnDuplicar.TabIndex = 2;
        btnDuplicar.Text = "\uD83D\uDCCB  Duplicar";
        btnDuplicar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnDuplicar.Padding = new Padding(8, 0, 0, 0);
        btnDuplicar.Click += btnDuplicar_Click;
        //
        // btnExcluir
        //
        btnExcluir.BackColor = System.Drawing.Color.FromArgb(200, 50, 50);
        btnExcluir.ForeColor = System.Drawing.Color.White;
        btnExcluir.Margin = new Padding(0, 0, 0, 6);
        btnExcluir.Name = "btnExcluir";
        btnExcluir.Size = new System.Drawing.Size(130, 30);
        btnExcluir.TabIndex = 3;
        btnExcluir.Text = "\u2716  Excluir";
        btnExcluir.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnExcluir.Padding = new Padding(8, 0, 0, 0);
        btnExcluir.Click += btnExcluir_Click;
        //
        // btnExecutar
        //
        btnExecutar.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
        btnExecutar.ForeColor = System.Drawing.Color.White;
        btnExecutar.Margin = new Padding(0, 0, 0, 6);
        btnExecutar.Name = "btnExecutar";
        btnExecutar.Size = new System.Drawing.Size(130, 30);
        btnExecutar.TabIndex = 4;
        btnExecutar.Text = "\u25B6  Executar";
        btnExecutar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnExecutar.Padding = new Padding(8, 0, 0, 0);
        btnExecutar.Click += btnExecutar_Click;
        //
        // btnParar
        //
        btnParar.BackColor = System.Drawing.Color.FromArgb(200, 50, 50);
        btnParar.ForeColor = System.Drawing.Color.White;
        btnParar.Margin = new Padding(0, 0, 0, 6);
        btnParar.Name = "btnParar";
        btnParar.Size = new System.Drawing.Size(130, 30);
        btnParar.TabIndex = 5;
        btnParar.Text = "\u23F9  Parar";
        btnParar.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnParar.Padding = new Padding(8, 0, 0, 0);
        btnParar.Click += btnParar_Click;
        //
        // btnSalvarPerfil
        //
        btnSalvarPerfil.Margin = new Padding(0, 0, 0, 6);
        btnSalvarPerfil.Name = "btnSalvarPerfil";
        btnSalvarPerfil.Size = new System.Drawing.Size(130, 30);
        btnSalvarPerfil.TabIndex = 6;
        btnSalvarPerfil.Text = "\uD83D\uDCBE  Salvar perfil";
        btnSalvarPerfil.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnSalvarPerfil.Padding = new Padding(8, 0, 0, 0);
        btnSalvarPerfil.Click += btnSalvarPerfil_Click;
        //
        // btnExecutarPerfil
        //
        btnExecutarPerfil.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
        btnExecutarPerfil.ForeColor = System.Drawing.Color.White;
        btnExecutarPerfil.Margin = new Padding(0, 0, 0, 6);
        btnExecutarPerfil.Name = "btnExecutarPerfil";
        btnExecutarPerfil.Size = new System.Drawing.Size(130, 30);
        btnExecutarPerfil.TabIndex = 7;
        btnExecutarPerfil.Text = "\u25B6  Exec. perfil";
        btnExecutarPerfil.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnExecutarPerfil.Padding = new Padding(8, 0, 0, 0);
        btnExecutarPerfil.Click += btnExecutarPerfil_Click;
        //
        // btnPararPerfil
        //
        btnPararPerfil.BackColor = System.Drawing.Color.FromArgb(200, 50, 50);
        btnPararPerfil.ForeColor = System.Drawing.Color.White;
        btnPararPerfil.Margin = new Padding(0, 0, 0, 6);
        btnPararPerfil.Name = "btnPararPerfil";
        btnPararPerfil.Size = new System.Drawing.Size(130, 30);
        btnPararPerfil.TabIndex = 8;
        btnPararPerfil.Text = "\u23F9  Parar perfil";
        btnPararPerfil.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnPararPerfil.Padding = new Padding(8, 0, 0, 0);
        btnPararPerfil.Click += btnPararPerfil_Click;
        //
        // btnTestarItem
        //
        btnTestarItem.BackColor = System.Drawing.Color.FromArgb(255, 152, 0);
        btnTestarItem.ForeColor = System.Drawing.Color.White;
        btnTestarItem.Margin = new Padding(0, 0, 0, 6);
        btnTestarItem.Name = "btnTestarItem";
        btnTestarItem.Size = new System.Drawing.Size(130, 30);
        btnTestarItem.TabIndex = 9;
        btnTestarItem.Text = "\uD83E\uDDEA  Testar item";
        btnTestarItem.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        btnTestarItem.Padding = new Padding(8, 0, 0, 0);
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
        Icon = new System.Drawing.Icon(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Properties", "app.ico"));
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
