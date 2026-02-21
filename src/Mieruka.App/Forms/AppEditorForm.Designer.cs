#nullable enable
using System.ComponentModel;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Services.Ui;
using Mieruka.App.Ui.PreviewBindings;

namespace Mieruka.App.Forms;

partial class AppEditorForm
{
    private IContainer? components = null;
    internal TabControl tabEditor = null!;
    internal TabPage tpGeral = null!;
    internal TabPage tabAplicativos = null!;
    internal TabPage tpJanela = null!;
    internal TabPage tabSites = null!;
    internal TabPage tpCiclo = null!;
    internal TabPage tpAvancado = null!;
    internal SitesEditorControl sitesEditorControl = null!;
    internal ComboBox cmbBrowserEngine = null!;
    internal Label lblBrowserDetected = null!;
    internal Panel pnlBrowserPanel = null!;
    internal AppsTab appsTabControl = null!;
    internal TableLayoutPanel tlpAplicativos = null!;
    internal Button btnSalvar = null!;
    internal Button btnCancelar = null!;
    internal Button btnTestarJanela = null!;
    internal Button btnTestReal = null!;
    internal Button btnCyclePlay = null!;
    internal Button btnCycleStep = null!;
    internal Button btnCycleStop = null!;
    internal TextBox txtId = null!;
    internal TextBox txtExecutavel = null!;
    internal ComboBox cmbNavegadores = null!;
    internal TextBox txtArgumentos = null!;
    internal TextBox txtCmdPreviewExe = null!;
    internal RadioButton rbExe = null!;
    internal RadioButton rbBrowser = null!;
    internal Button btnBrowseExe = null!;
    internal CheckBox chkAutoStart = null!;
    internal NumericUpDown nudJanelaX = null!;
    internal NumericUpDown nudJanelaY = null!;
    internal NumericUpDown nudJanelaLargura = null!;
    internal NumericUpDown nudJanelaAltura = null!;
    internal CheckBox chkJanelaTelaCheia = null!;
    internal ComboBox cboMonitores = null!;
    internal MonitorPreviewDisplay monitorPreviewDisplay = null!;
    internal TableLayoutPanel tlpMonitorPreview = null!;
    internal Label lblMonitorCoordinates = null!;
    internal TableLayoutPanel tlpCycle = null!;
    internal DataGridView dgvCycle = null!;
    internal BindingSource bsCycle = null!;
    internal FlowLayoutPanel flpCycleButtons = null!;
    internal Button btnCycleUp = null!;
    internal Button btnCycleDown = null!;
    internal ErrorProvider errorProvider = null!;
    internal FlowLayoutPanel flowCycleControls = null!;
    internal FlowLayoutPanel flowCycleItems = null!;
    internal CheckBox chkCycleRedeDisponivel = null!;
    internal ToolTip cycleToolTip = null!;
    internal TextBox txtNomeAmigavel = null!;
    internal TextBox txtEnvVars = null!;
    internal CheckBox chkWatchdogEnabled = null!;
    internal NumericUpDown nudWatchdogGrace = null!;
    internal ToolTip editorToolTip = null!;
    internal TextBox txtWindowTitle = null!;
    internal CheckBox chkAlwaysOnTop = null!;
    internal ComboBox cmbHealthCheckType = null!;
    internal TextBox txtHealthCheckUrl = null!;
    internal TextBox txtHealthCheckDomSelector = null!;
    internal TextBox txtHealthCheckContainsText = null!;
    internal NumericUpDown nudHealthCheckInterval = null!;
    internal NumericUpDown nudHealthCheckTimeout = null!;

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
        bsCycle = new BindingSource(components);
        tabEditor = new TabControl();
        tpGeral = new TabPage();
        tabAplicativos = new TabPage();
        tpJanela = new TabPage();
        tabSites = new TabPage();
        tpCiclo = new TabPage();
        tpAvancado = new TabPage();
        sitesEditorControl = new SitesEditorControl();
        cmbBrowserEngine = new ComboBox();
        lblBrowserDetected = new Label();
        pnlBrowserPanel = new Panel();
        appsTabControl = new AppsTab();
        tlpAplicativos = new TableLayoutPanel();
        var tlpSites = new TableLayoutPanel();
        var flowBrowserHeader = new FlowLayoutPanel();
        var lblBrowserEngine = new Label();
        var painelRodape = new FlowLayoutPanel();
        btnSalvar = new Button();
        btnCancelar = new Button();
        btnTestarJanela = new Button();
        btnTestReal = new Button();
        var tlpGeral = new TableLayoutPanel();
        var lblId = new Label();
        txtId = new TextBox();
        var lblNavegadores = new Label();
        var lblExecutavel = new Label();
        txtExecutavel = new TextBox();
        cmbNavegadores = new ComboBox();
        var lblArgumentos = new Label();
        txtArgumentos = new TextBox();
        var lblCmdPreviewExe = new Label();
        txtCmdPreviewExe = new TextBox();
        chkAutoStart = new CheckBox();
        rbExe = new RadioButton();
        rbBrowser = new RadioButton();
        btnBrowseExe = new Button();
        var flowAppType = new FlowLayoutPanel();
        var tlpExecutavel = new TableLayoutPanel();
        var tlpJanela = new TableLayoutPanel();
        tlpMonitorPreview = new TableLayoutPanel();
        var lblMonitor = new Label();
        cboMonitores = new ComboBox();
        monitorPreviewDisplay = new MonitorPreviewDisplay();
        chkJanelaTelaCheia = new CheckBox();
        var lblX = new Label();
        nudJanelaX = new NumericUpDown();
        var lblY = new Label();
        nudJanelaY = new NumericUpDown();
        var lblLargura = new Label();
        nudJanelaLargura = new NumericUpDown();
        var lblAltura = new Label();
        lblMonitorCoordinates = new Label();
        nudJanelaAltura = new NumericUpDown();
        tlpCycle = new TableLayoutPanel();
        var tlpCycleList = new TableLayoutPanel();
        dgvCycle = new DataGridView();
        flowCycleControls = new FlowLayoutPanel();
        btnCyclePlay = new Button();
        btnCycleStep = new Button();
        btnCycleStop = new Button();
        chkCycleRedeDisponivel = new CheckBox();
        flpCycleButtons = new FlowLayoutPanel();
        btnCycleUp = new Button();
        btnCycleDown = new Button();
        flowCycleItems = new FlowLayoutPanel();
        errorProvider = new ErrorProvider(components);
        cycleToolTip = ToolTipTamer.Create(components);
        editorToolTip = ToolTipTamer.Create(components);
        txtNomeAmigavel = new TextBox();
        txtEnvVars = new TextBox();
        chkWatchdogEnabled = new CheckBox();
        nudWatchdogGrace = new NumericUpDown();
        txtWindowTitle = new TextBox();
        chkAlwaysOnTop = new CheckBox();
        cmbHealthCheckType = new ComboBox();
        txtHealthCheckUrl = new TextBox();
        txtHealthCheckDomSelector = new TextBox();
        txtHealthCheckContainsText = new TextBox();
        nudHealthCheckInterval = new NumericUpDown();
        nudHealthCheckTimeout = new NumericUpDown();
        var tlpAvancado = new TableLayoutPanel();
        var lblNomeAmigavel = new Label();
        var lblWindowTitle = new Label();
        var lblEnvVars = new Label();
        var lblWatchdogGrace = new Label();
        var lblHealthCheckType = new Label();
        var lblHealthCheckUrl = new Label();
        var lblHealthCheckDomSelector = new Label();
        var lblHealthCheckContainsText = new Label();
        var lblHealthCheckInterval = new Label();
        var lblHealthCheckTimeout = new Label();
        ((ISupportInitialize)nudWatchdogGrace).BeginInit();
        ((ISupportInitialize)nudHealthCheckInterval).BeginInit();
        ((ISupportInitialize)nudHealthCheckTimeout).BeginInit();
        tlpExecutavel.SuspendLayout();
        flowAppType.SuspendLayout();
        ((ISupportInitialize)bsCycle).BeginInit();
        ((ISupportInitialize)dgvCycle).BeginInit();
        tabEditor.SuspendLayout();
        tpGeral.SuspendLayout();
        tabAplicativos.SuspendLayout();
        tpJanela.SuspendLayout();
        tabSites.SuspendLayout();
        pnlBrowserPanel.SuspendLayout();
        tlpCycle.SuspendLayout();
        tlpCycleList.SuspendLayout();
        flowCycleControls.SuspendLayout();
        flpCycleButtons.SuspendLayout();
        flowCycleItems.SuspendLayout();
        tpCiclo.SuspendLayout();
        tpAvancado.SuspendLayout();
        painelRodape.SuspendLayout();
        tlpGeral.SuspendLayout();
        tlpJanela.SuspendLayout();
        tlpMonitorPreview.SuspendLayout();
        ((ISupportInitialize)nudJanelaX).BeginInit();
        ((ISupportInitialize)nudJanelaY).BeginInit();
        ((ISupportInitialize)nudJanelaLargura).BeginInit();
        ((ISupportInitialize)nudJanelaAltura).BeginInit();
        ((ISupportInitialize)errorProvider).BeginInit();
        SuspendLayout();
        //
        // tabEditor
        //
        tabEditor.Controls.Add(tpGeral);
        tabEditor.Controls.Add(tabAplicativos);
        tabEditor.Controls.Add(tpJanela);
        tabEditor.Controls.Add(tabSites);
        tabEditor.Controls.Add(tpCiclo);
        tabEditor.Controls.Add(tpAvancado);
        tabEditor.Dock = DockStyle.Fill;
        tabEditor.Location = new System.Drawing.Point(0, 0);
        tabEditor.Margin = new Padding(8);
        tabEditor.Name = "tabEditor";
        tabEditor.SelectedIndex = 0;
        tabEditor.Size = new System.Drawing.Size(1040, 648);
        tabEditor.TabIndex = 0;
        //
        // tpGeral
        //
        tpGeral.Controls.Add(tlpGeral);
        tpGeral.Location = new System.Drawing.Point(4, 24);
        tpGeral.Margin = new Padding(8);
        tpGeral.Name = "tpGeral";
        tpGeral.Padding = new Padding(8);
        tpGeral.Size = new System.Drawing.Size(1032, 620);
        tpGeral.TabIndex = 0;
        tpGeral.Text = "Geral";
        tpGeral.UseVisualStyleBackColor = true;
        //
        // tabAplicativos
        //
        tabAplicativos.Controls.Add(tlpAplicativos);
        tabAplicativos.Location = new System.Drawing.Point(4, 24);
        tabAplicativos.Margin = new Padding(8);
        tabAplicativos.Name = "tabAplicativos";
        tabAplicativos.Padding = new Padding(8);
        tabAplicativos.Size = new System.Drawing.Size(1032, 620);
        tabAplicativos.TabIndex = 1;
        tabAplicativos.Text = "Aplicativos";
        tabAplicativos.UseVisualStyleBackColor = true;
        //
        // tlpAplicativos
        //
        tlpAplicativos.ColumnCount = 1;
        tlpAplicativos.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpAplicativos.RowStyles.Clear();
        tlpAplicativos.Controls.Add(appsTabControl, 0, 0);
        tlpAplicativos.Dock = DockStyle.Fill;
        tlpAplicativos.Location = new System.Drawing.Point(8, 8);
        tlpAplicativos.Margin = new Padding(0);
        tlpAplicativos.Name = "tlpAplicativos";
        tlpAplicativos.RowCount = 1;
        tlpAplicativos.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpAplicativos.Size = new System.Drawing.Size(1016, 604);
        tlpAplicativos.TabIndex = 0;
        //
        // appsTabControl
        //
        appsTabControl.Dock = DockStyle.Fill;
        appsTabControl.Margin = new Padding(0);
        appsTabControl.Name = "appsTabControl";
        appsTabControl.TabIndex = 1;
        //
        // tlpGeral
        //
        tlpGeral.ColumnCount = 2;
        tlpGeral.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpGeral.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpGeral.Controls.Add(lblId, 0, 0);
        tlpGeral.Controls.Add(txtId, 1, 0);
        tlpGeral.Controls.Add(flowAppType, 0, 1);
        tlpGeral.Controls.Add(lblNavegadores, 0, 2);
        tlpGeral.Controls.Add(cmbNavegadores, 1, 2);
        tlpGeral.Controls.Add(lblExecutavel, 0, 3);
        tlpGeral.Controls.Add(tlpExecutavel, 1, 3);
        tlpGeral.Controls.Add(lblArgumentos, 0, 4);
        tlpGeral.Controls.Add(txtArgumentos, 1, 4);
        tlpGeral.Controls.Add(lblCmdPreviewExe, 0, 5);
        tlpGeral.Controls.Add(txtCmdPreviewExe, 1, 5);
        tlpGeral.Controls.Add(chkAutoStart, 1, 6);
        tlpGeral.SetColumnSpan(flowAppType, 2);
        tlpGeral.Dock = DockStyle.Fill;
        tlpGeral.Location = new System.Drawing.Point(8, 8);
        tlpGeral.Margin = new Padding(0);
        tlpGeral.Name = "tlpGeral";
        tlpGeral.Padding = new Padding(0, 0, 0, 8);
        tlpGeral.RowCount = 7;
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.Size = new System.Drawing.Size(1016, 604);
        tlpGeral.TabIndex = 0;
        //
        // lblId
        //
        lblId.AutoSize = true;
        lblId.Margin = new Padding(0, 0, 8, 8);
        lblId.Name = "lblId";
        lblId.Size = new System.Drawing.Size(21, 15);
        lblId.TabIndex = 0;
        lblId.Text = "ID";
        //
        // txtId
        //
        txtId.Dock = DockStyle.Fill;
        txtId.Margin = new Padding(0, 0, 0, 8);
        txtId.Name = "txtId";
        txtId.Size = new System.Drawing.Size(1016, 23);
        txtId.TabIndex = 1;
        //
        // flowAppType
        //
        flowAppType.AutoSize = true;
        flowAppType.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flowAppType.Dock = DockStyle.Fill;
        flowAppType.FlowDirection = FlowDirection.LeftToRight;
        flowAppType.Location = new System.Drawing.Point(0, 31);
        flowAppType.Margin = new Padding(0, 0, 0, 8);
        flowAppType.Name = "flowAppType";
        flowAppType.Size = new System.Drawing.Size(1016, 27);
        flowAppType.TabIndex = 2;
        flowAppType.WrapContents = false;
        flowAppType.Controls.Add(rbExe);
        flowAppType.Controls.Add(rbBrowser);
        //
        // lblNavegadores
        //
        lblNavegadores.AutoSize = true;
        lblNavegadores.Margin = new Padding(0, 0, 8, 8);
        lblNavegadores.Name = "lblNavegadores";
        lblNavegadores.Size = new System.Drawing.Size(73, 15);
        lblNavegadores.TabIndex = 4;
        lblNavegadores.Text = "Navegadores";
        //
        // cmbNavegadores
        //
        cmbNavegadores.Dock = DockStyle.Fill;
        cmbNavegadores.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbNavegadores.FormattingEnabled = true;
        cmbNavegadores.Margin = new Padding(0, 0, 0, 8);
        cmbNavegadores.Name = "cmbNavegadores";
        cmbNavegadores.Size = new System.Drawing.Size(942, 23);
        cmbNavegadores.TabIndex = 5;
        cmbNavegadores.SelectedIndexChanged += cmbNavegadores_SelectedIndexChanged;
        //
        // rbExe
        //
        rbExe.AutoSize = true;
        rbExe.Checked = true;
        rbExe.Margin = new Padding(0, 0, 16, 0);
        rbExe.Name = "rbExe";
        rbExe.Size = new System.Drawing.Size(83, 19);
        rbExe.TabIndex = 0;
        rbExe.TabStop = true;
        rbExe.Text = "Aplicativo";
        rbExe.UseVisualStyleBackColor = true;
        rbExe.CheckedChanged += rbExe_CheckedChanged;
        //
        // rbBrowser
        //
        rbBrowser.AutoSize = true;
        rbBrowser.Margin = new Padding(0);
        rbBrowser.Name = "rbBrowser";
        rbBrowser.Size = new System.Drawing.Size(73, 19);
        rbBrowser.TabIndex = 1;
        rbBrowser.Text = "Navegador";
        rbBrowser.UseVisualStyleBackColor = true;
        rbBrowser.CheckedChanged += rbBrowser_CheckedChanged;
        //
        // lblExecutavel
        //
        lblExecutavel.AutoSize = true;
        lblExecutavel.Margin = new Padding(0, 0, 8, 8);
        lblExecutavel.Name = "lblExecutavel";
        lblExecutavel.Size = new System.Drawing.Size(66, 15);
        lblExecutavel.TabIndex = 6;
        lblExecutavel.Text = "Aplicativo";
        //
        // tlpExecutavel
        //
        tlpExecutavel.ColumnCount = 2;
        tlpExecutavel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpExecutavel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpExecutavel.Controls.Add(txtExecutavel, 0, 0);
        tlpExecutavel.Controls.Add(btnBrowseExe, 1, 0);
        tlpExecutavel.Dock = DockStyle.Fill;
        tlpExecutavel.Location = new System.Drawing.Point(74, 283);
        tlpExecutavel.Margin = new Padding(0, 0, 0, 8);
        tlpExecutavel.Name = "tlpExecutavel";
        tlpExecutavel.RowCount = 1;
        tlpExecutavel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpExecutavel.Size = new System.Drawing.Size(942, 31);
        tlpExecutavel.TabIndex = 7;
        //
        // txtExecutavel
        //
        txtExecutavel.Dock = DockStyle.Fill;
        txtExecutavel.Margin = new Padding(0);
        txtExecutavel.Name = "txtExecutavel";
        txtExecutavel.ReadOnly = true;
        txtExecutavel.Size = new System.Drawing.Size(842, 23);
        txtExecutavel.TabIndex = 0;
        txtExecutavel.TabStop = false;
        //
        // btnBrowseExe
        //
        btnBrowseExe.AutoSize = true;
        btnBrowseExe.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnBrowseExe.Margin = new Padding(8, 0, 0, 0);
        btnBrowseExe.Name = "btnBrowseExe";
        btnBrowseExe.Size = new System.Drawing.Size(80, 25);
        btnBrowseExe.TabIndex = 1;
        btnBrowseExe.Text = "Procurar...";
        btnBrowseExe.UseVisualStyleBackColor = true;
        editorToolTip.SetToolTip(btnBrowseExe, "Selecionar arquivo executável (.exe)");
        //
        // lblArgumentos
        //
        lblArgumentos.AutoSize = true;
        lblArgumentos.Margin = new Padding(0, 0, 8, 8);
        lblArgumentos.Name = "lblArgumentos";
        lblArgumentos.Size = new System.Drawing.Size(73, 15);
        lblArgumentos.TabIndex = 8;
        lblArgumentos.Text = "Argumentos";
        //
        // txtArgumentos
        //
        txtArgumentos.Dock = DockStyle.Fill;
        txtArgumentos.Margin = new Padding(0, 0, 0, 8);
        txtArgumentos.Multiline = true;
        txtArgumentos.Name = "txtArgumentos";
        txtArgumentos.ScrollBars = ScrollBars.Vertical;
        txtArgumentos.Size = new System.Drawing.Size(942, 347);
        txtArgumentos.TabIndex = 9;
        txtArgumentos.ReadOnly = true;
        txtArgumentos.TabStop = false;
        //
        // lblCmdPreviewExe
        //
        lblCmdPreviewExe.AutoSize = true;
        lblCmdPreviewExe.Margin = new Padding(0, 0, 8, 8);
        lblCmdPreviewExe.Name = "lblCmdPreviewExe";
        lblCmdPreviewExe.Size = new System.Drawing.Size(123, 15);
        lblCmdPreviewExe.TabIndex = 8;
        lblCmdPreviewExe.Text = "Linha de comando";
        //
        // txtCmdPreviewExe
        //
        txtCmdPreviewExe.Dock = DockStyle.Fill;
        txtCmdPreviewExe.Margin = new Padding(0, 0, 0, 8);
        txtCmdPreviewExe.Multiline = true;
        txtCmdPreviewExe.Name = "txtCmdPreviewExe";
        txtCmdPreviewExe.ReadOnly = true;
        txtCmdPreviewExe.ScrollBars = ScrollBars.Vertical;
        txtCmdPreviewExe.Size = new System.Drawing.Size(942, 96);
        txtCmdPreviewExe.TabIndex = 9;
        txtCmdPreviewExe.Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9F);
        //
        // chkAutoStart
        //
        chkAutoStart.AutoSize = true;
        chkAutoStart.Margin = new Padding(0, 0, 0, 8);
        chkAutoStart.Name = "chkAutoStart";
        chkAutoStart.Size = new System.Drawing.Size(127, 19);
        chkAutoStart.TabIndex = 10;
        chkAutoStart.Text = "Executar ao iniciar";
        chkAutoStart.UseVisualStyleBackColor = true;
        //
        // tpJanela
        //
        // tpJanela — WinForms z-order: last-added is docked first.
        // Add Fill first, then Right last, so preview reserves space before tlpJanela expands.
        tpJanela.Controls.Add(tlpJanela);
        tpJanela.Controls.Add(tlpMonitorPreview);
        tpJanela.Location = new System.Drawing.Point(4, 24);
        tpJanela.Margin = new Padding(8);
        tpJanela.Name = "tpJanela";
        tpJanela.Padding = new Padding(8);
        tpJanela.Size = new System.Drawing.Size(1032, 620);
        tpJanela.TabIndex = 2;
        tpJanela.Text = "Janela/Posição";
        tpJanela.UseVisualStyleBackColor = true;
        //
        // tlpJanela
        //
        tlpJanela.ColumnCount = 2;
        tlpJanela.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpJanela.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpJanela.Controls.Add(lblMonitor, 0, 0);
        tlpJanela.Controls.Add(cboMonitores, 1, 0);
        tlpJanela.Controls.Add(chkJanelaTelaCheia, 0, 1);
        tlpJanela.SetColumnSpan(chkJanelaTelaCheia, 2);
        tlpJanela.Controls.Add(lblX, 0, 2);
        tlpJanela.Controls.Add(nudJanelaX, 1, 2);
        tlpJanela.Controls.Add(lblY, 0, 3);
        tlpJanela.Controls.Add(nudJanelaY, 1, 3);
        tlpJanela.Controls.Add(lblLargura, 0, 4);
        tlpJanela.Controls.Add(nudJanelaLargura, 1, 4);
        tlpJanela.Controls.Add(lblAltura, 0, 5);
        tlpJanela.Controls.Add(nudJanelaAltura, 1, 5);
        tlpJanela.Controls.Add(btnTestarJanela, 1, 6);
        tlpJanela.Controls.Add(btnTestReal, 1, 7);
        tlpJanela.Dock = DockStyle.Fill;
        tlpJanela.Location = new System.Drawing.Point(8, 8);
        tlpJanela.Margin = new Padding(0);
        tlpJanela.Name = "tlpJanela";
        tlpJanela.Padding = new Padding(0, 0, 0, 8);
        tlpJanela.RowCount = 8;
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpJanela.Size = new System.Drawing.Size(1016, 604);
        tlpJanela.TabIndex = 0;
        //
        // lblMonitor
        //
        lblMonitor.AutoSize = true;
        lblMonitor.Margin = new Padding(0, 0, 8, 8);
        lblMonitor.Name = "lblMonitor";
        lblMonitor.Size = new System.Drawing.Size(52, 15);
        lblMonitor.TabIndex = 0;
        lblMonitor.Text = "Monitor";
        //
        // cboMonitores
        //
        cboMonitores.Dock = DockStyle.Fill;
        cboMonitores.DropDownStyle = ComboBoxStyle.DropDownList;
        cboMonitores.FormattingEnabled = true;
        cboMonitores.Margin = new Padding(0, 0, 0, 8);
        cboMonitores.Name = "cboMonitores";
        cboMonitores.Size = new System.Drawing.Size(768, 23);
        cboMonitores.TabIndex = 0;
        //
        // tlpMonitorPreview
        //
        tlpMonitorPreview.ColumnCount = 1;
        tlpMonitorPreview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpMonitorPreview.Controls.Add(monitorPreviewDisplay, 0, 0);
        tlpMonitorPreview.Controls.Add(lblMonitorCoordinates, 0, 1);
        tlpMonitorPreview.Dock = DockStyle.Right;
        tlpMonitorPreview.Location = new System.Drawing.Point(592, 8);
        tlpMonitorPreview.Margin = new Padding(8, 0, 0, 8);
        tlpMonitorPreview.MinimumSize = new System.Drawing.Size(420, 0);
        tlpMonitorPreview.Name = "tlpMonitorPreview";
        tlpMonitorPreview.Padding = new Padding(8);
        tlpMonitorPreview.RowCount = 2;
        tlpMonitorPreview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpMonitorPreview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpMonitorPreview.Size = new System.Drawing.Size(440, 604);
        tlpMonitorPreview.TabIndex = 10;
        //
        // monitorPreviewDisplay
        //
        monitorPreviewDisplay.BackColor = System.Drawing.Color.FromArgb(176, 176, 176);
        monitorPreviewDisplay.Dock = DockStyle.Fill;
        monitorPreviewDisplay.Location = new System.Drawing.Point(8, 8);
        monitorPreviewDisplay.Margin = new Padding(0, 0, 0, 8);
        monitorPreviewDisplay.Name = "monitorPreviewDisplay";
        monitorPreviewDisplay.Size = new System.Drawing.Size(424, 546);
        monitorPreviewDisplay.TabIndex = 7;
        monitorPreviewDisplay.TabStop = false;
        //
        // lblMonitorCoordinates
        //
        lblMonitorCoordinates.AutoSize = true;
        lblMonitorCoordinates.Dock = DockStyle.Fill;
        lblMonitorCoordinates.Location = new System.Drawing.Point(8, 562);
        lblMonitorCoordinates.Margin = new Padding(0);
        lblMonitorCoordinates.Name = "lblMonitorCoordinates";
        lblMonitorCoordinates.Padding = new Padding(0, 4, 0, 0);
        lblMonitorCoordinates.Size = new System.Drawing.Size(424, 34);
        lblMonitorCoordinates.TabIndex = 8;
        lblMonitorCoordinates.Text = "X=–, Y=–";
        lblMonitorCoordinates.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        //
        // chkJanelaTelaCheia
        //
        chkJanelaTelaCheia.AutoSize = true;
        chkJanelaTelaCheia.Margin = new Padding(0, 0, 8, 8);
        chkJanelaTelaCheia.Name = "chkJanelaTelaCheia";
        chkJanelaTelaCheia.Size = new System.Drawing.Size(147, 19);
        chkJanelaTelaCheia.TabIndex = 1;
        chkJanelaTelaCheia.Text = "Utilizar tela inteira";
        chkJanelaTelaCheia.UseVisualStyleBackColor = true;
        //
        // lblX
        //
        lblX.AutoSize = true;
        lblX.Margin = new Padding(0, 0, 8, 8);
        lblX.Name = "lblX";
        lblX.Size = new System.Drawing.Size(15, 15);
        lblX.TabIndex = 2;
        lblX.Text = "X";
        //
        // nudJanelaX
        //
        nudJanelaX.Margin = new Padding(0, 0, 8, 8);
        nudJanelaX.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        nudJanelaX.Minimum = new decimal(new int[] { 10000, 0, 0, int.MinValue });
        nudJanelaX.Name = "nudJanelaX";
        nudJanelaX.Size = new System.Drawing.Size(160, 23);
        nudJanelaX.TabIndex = 2;
        //
        // lblY
        //
        lblY.AutoSize = true;
        lblY.Margin = new Padding(0, 0, 8, 8);
        lblY.Name = "lblY";
        lblY.Size = new System.Drawing.Size(15, 15);
        lblY.TabIndex = 3;
        lblY.Text = "Y";
        //
        // nudJanelaY
        //
        nudJanelaY.Margin = new Padding(0, 0, 8, 8);
        nudJanelaY.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        nudJanelaY.Minimum = new decimal(new int[] { 10000, 0, 0, int.MinValue });
        nudJanelaY.Name = "nudJanelaY";
        nudJanelaY.Size = new System.Drawing.Size(160, 23);
        nudJanelaY.TabIndex = 4;
        //
        // lblLargura
        //
        lblLargura.AutoSize = true;
        lblLargura.Margin = new Padding(0, 0, 8, 8);
        lblLargura.Name = "lblLargura";
        lblLargura.Size = new System.Drawing.Size(48, 15);
        lblLargura.TabIndex = 5;
        lblLargura.Text = "Largura";
        //
        // nudJanelaLargura
        //
        nudJanelaLargura.Margin = new Padding(0, 0, 8, 8);
        nudJanelaLargura.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        nudJanelaLargura.Name = "nudJanelaLargura";
        nudJanelaLargura.Size = new System.Drawing.Size(160, 23);
        nudJanelaLargura.TabIndex = 6;
        //
        // lblAltura
        //
        lblAltura.AutoSize = true;
        lblAltura.Margin = new Padding(0, 0, 8, 8);
        lblAltura.Name = "lblAltura";
        lblAltura.Size = new System.Drawing.Size(42, 15);
        lblAltura.TabIndex = 7;
        lblAltura.Text = "Altura";
        //
        // nudJanelaAltura
        //
        nudJanelaAltura.Margin = new Padding(0, 0, 8, 8);
        nudJanelaAltura.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        nudJanelaAltura.Name = "nudJanelaAltura";
        nudJanelaAltura.Size = new System.Drawing.Size(160, 23);
        nudJanelaAltura.TabIndex = 8;
        //
        // btnTestarJanela
        //
        btnTestarJanela.AutoSize = true;
        btnTestarJanela.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestarJanela.Margin = new Padding(0, 0, 8, 8);
        btnTestarJanela.Name = "btnTestarJanela";
        btnTestarJanela.Size = new System.Drawing.Size(52, 25);
        btnTestarJanela.TabIndex = 9;
        btnTestarJanela.Text = "Testar";
        btnTestarJanela.UseVisualStyleBackColor = true;
        btnTestarJanela.Click += btnTestarJanela_Click;
        editorToolTip.SetToolTip(btnTestarJanela, "Simular posicionamento da janela no monitor selecionado");
        //
        // btnTestReal
        //
        btnTestReal.AutoSize = true;
        btnTestReal.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnTestReal.Margin = new Padding(0, 0, 8, 8);
        btnTestReal.Name = "btnTestReal";
        btnTestReal.Size = new System.Drawing.Size(88, 25);
        btnTestReal.TabIndex = 10;
        btnTestReal.Text = "Executar real";
        btnTestReal.UseVisualStyleBackColor = true;
        btnTestReal.Click += btnTestReal_Click;
        editorToolTip.SetToolTip(btnTestReal, "Iniciar o programa e posicionar a janela real");
        //
        // tabSites
        //
        tabSites.Controls.Add(tlpSites);
        tabSites.Location = new System.Drawing.Point(4, 24);
        tabSites.Margin = new Padding(8);
        tabSites.Name = "tabSites";
        tabSites.Padding = new Padding(8);
        tabSites.Size = new System.Drawing.Size(1032, 620);
        tabSites.TabIndex = 3;
        tabSites.Text = "Sites";
        tabSites.UseVisualStyleBackColor = true;
        //
        // tlpSites
        //
        tlpSites.ColumnCount = 1;
        tlpSites.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpSites.Controls.Add(flowBrowserHeader, 0, 0);
        tlpSites.Controls.Add(lblBrowserDetected, 0, 1);
        tlpSites.Controls.Add(pnlBrowserPanel, 0, 2);
        tlpSites.Dock = DockStyle.Fill;
        tlpSites.Location = new System.Drawing.Point(8, 8);
        tlpSites.Margin = new Padding(0);
        tlpSites.Name = "tlpSites";
        tlpSites.RowCount = 3;
        tlpSites.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpSites.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpSites.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpSites.Size = new System.Drawing.Size(1016, 604);
        tlpSites.TabIndex = 0;
        //
        // flowBrowserHeader
        //
        flowBrowserHeader.AutoSize = true;
        flowBrowserHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flowBrowserHeader.Dock = DockStyle.Fill;
        flowBrowserHeader.FlowDirection = FlowDirection.LeftToRight;
        flowBrowserHeader.Location = new System.Drawing.Point(0, 0);
        flowBrowserHeader.Margin = new Padding(0, 0, 0, 8);
        flowBrowserHeader.Name = "flowBrowserHeader";
        flowBrowserHeader.WrapContents = false;
        flowBrowserHeader.Controls.Add(lblBrowserEngine);
        flowBrowserHeader.Controls.Add(cmbBrowserEngine);
        //
        // lblBrowserEngine
        //
        lblBrowserEngine.AutoSize = true;
        lblBrowserEngine.Margin = new Padding(0, 0, 8, 0);
        lblBrowserEngine.Name = "lblBrowserEngine";
        lblBrowserEngine.Size = new System.Drawing.Size(113, 15);
        lblBrowserEngine.TabIndex = 0;
        lblBrowserEngine.Text = "Motor do navegador";
        //
        // cmbBrowserEngine
        //
        cmbBrowserEngine.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbBrowserEngine.FormattingEnabled = true;
        cmbBrowserEngine.Margin = new Padding(0);
        cmbBrowserEngine.MinimumSize = new System.Drawing.Size(200, 0);
        cmbBrowserEngine.Name = "cmbBrowserEngine";
        cmbBrowserEngine.Size = new System.Drawing.Size(200, 23);
        cmbBrowserEngine.TabIndex = 1;
        //
        // lblBrowserDetected
        //
        lblBrowserDetected.AutoSize = true;
        lblBrowserDetected.ForeColor = System.Drawing.SystemColors.GrayText;
        lblBrowserDetected.Margin = new Padding(0, 0, 0, 8);
        lblBrowserDetected.Name = "lblBrowserDetected";
        lblBrowserDetected.Size = new System.Drawing.Size(0, 15);
        lblBrowserDetected.TabIndex = 1;
        lblBrowserDetected.Visible = false;
        //
        // pnlBrowserPanel
        //
        pnlBrowserPanel.Controls.Add(sitesEditorControl);
        pnlBrowserPanel.Dock = DockStyle.Fill;
        pnlBrowserPanel.Location = new System.Drawing.Point(0, 46);
        pnlBrowserPanel.Margin = new Padding(0);
        pnlBrowserPanel.Name = "pnlBrowserPanel";
        pnlBrowserPanel.Size = new System.Drawing.Size(1016, 558);
        pnlBrowserPanel.TabIndex = 2;
        //
        // sitesEditorControl
        //
        sitesEditorControl.Dock = DockStyle.Fill;
        sitesEditorControl.Location = new System.Drawing.Point(0, 0);
        sitesEditorControl.Margin = new Padding(0);
        sitesEditorControl.Name = "sitesEditorControl";
        sitesEditorControl.Size = new System.Drawing.Size(1016, 558);
        sitesEditorControl.TabIndex = 0;
        //
        // tlpCycle
        //
        tlpCycle.ColumnCount = 1;
        tlpCycle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpCycle.Controls.Add(flowCycleControls, 0, 0);
        tlpCycle.Controls.Add(tlpCycleList, 0, 1);
        tlpCycle.Controls.Add(flowCycleItems, 0, 2);
        tlpCycle.Dock = DockStyle.Fill;
        tlpCycle.Location = new System.Drawing.Point(8, 8);
        tlpCycle.Margin = new Padding(0);
        tlpCycle.Name = "tlpCycle";
        tlpCycle.RowCount = 3;
        tlpCycle.RowStyles.Add(new RowStyle());
        tlpCycle.RowStyles.Add(new RowStyle(SizeType.Absolute, 200F));
        tlpCycle.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpCycle.Size = new System.Drawing.Size(1016, 604);
        tlpCycle.TabIndex = 0;
        //
        // tlpCycleList
        //
        tlpCycleList.ColumnCount = 2;
        tlpCycleList.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpCycleList.ColumnStyles.Add(new ColumnStyle());
        tlpCycleList.Controls.Add(dgvCycle, 0, 0);
        tlpCycleList.Controls.Add(flpCycleButtons, 1, 0);
        tlpCycleList.Dock = DockStyle.Fill;
        tlpCycleList.Location = new System.Drawing.Point(0, 33);
        tlpCycleList.Margin = new Padding(0, 0, 0, 8);
        tlpCycleList.Name = "tlpCycleList";
        tlpCycleList.RowCount = 1;
        tlpCycleList.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        tlpCycleList.Size = new System.Drawing.Size(1016, 200);
        tlpCycleList.TabIndex = 1;
        //
        // dgvCycle
        //
        dgvCycle.AllowUserToAddRows = false;
        dgvCycle.AllowUserToDeleteRows = false;
        dgvCycle.AllowUserToResizeRows = false;
        dgvCycle.AutoGenerateColumns = false;
        dgvCycle.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
        dgvCycle.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        var colCycleOrder = new DataGridViewTextBoxColumn();
        var colCycleId = new DataGridViewTextBoxColumn();
        var colCycleDelay = new DataGridViewTextBoxColumn();
        var colCycleAsk = new DataGridViewCheckBoxColumn();
        var colCycleRequiresNetwork = new DataGridViewCheckBoxColumn();
        colCycleOrder.DataPropertyName = "Order";
        colCycleOrder.HeaderText = "Ordem";
        colCycleOrder.MinimumWidth = 80;
        colCycleOrder.Name = "colCycleOrder";
        colCycleOrder.Width = 80;
        colCycleId.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        colCycleId.DataPropertyName = "Id";
        colCycleId.HeaderText = "Programa";
        colCycleId.Name = "colCycleId";
        colCycleDelay.DataPropertyName = "DelayMs";
        colCycleDelay.HeaderText = "Atraso (ms)";
        colCycleDelay.MinimumWidth = 90;
        colCycleDelay.Name = "colCycleDelay";
        colCycleDelay.Width = 90;
        colCycleAsk.DataPropertyName = "AskBeforeLaunch";
        colCycleAsk.HeaderText = "Confirmar";
        colCycleAsk.MinimumWidth = 80;
        colCycleAsk.Name = "colCycleAsk";
        colCycleAsk.Width = 80;
        colCycleRequiresNetwork.DataPropertyName = "RequiresNetwork";
        colCycleRequiresNetwork.HeaderText = "Rede";
        colCycleRequiresNetwork.MinimumWidth = 70;
        colCycleRequiresNetwork.Name = "colCycleRequiresNetwork";
        colCycleRequiresNetwork.ThreeState = true;
        colCycleRequiresNetwork.Width = 70;
        dgvCycle.Columns.AddRange(new DataGridViewColumn[] { colCycleOrder, colCycleId, colCycleDelay, colCycleAsk, colCycleRequiresNetwork });
        dgvCycle.DataSource = bsCycle;
        dgvCycle.Dock = DockStyle.Fill;
        dgvCycle.Location = new System.Drawing.Point(0, 0);
        dgvCycle.Margin = new Padding(0);
        dgvCycle.MultiSelect = false;
        dgvCycle.Name = "dgvCycle";
        dgvCycle.RowHeadersVisible = false;
        dgvCycle.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvCycle.Size = new System.Drawing.Size(936, 200);
        dgvCycle.TabIndex = 0;
        dgvCycle.SelectionChanged += dgvCycle_SelectionChanged;
        dgvCycle.CurrentCellDirtyStateChanged += dgvCycle_CurrentCellDirtyStateChanged;
        dgvCycle.CellEndEdit += dgvCycle_CellEndEdit;
        dgvCycle.DataError += dgvCycle_DataError;
        //
        // flowCycleControls
        //
        flowCycleControls.AutoSize = true;
        flowCycleControls.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flowCycleControls.Dock = DockStyle.Fill;
        flowCycleControls.FlowDirection = FlowDirection.LeftToRight;
        flowCycleControls.Margin = new Padding(0, 0, 0, 8);
        flowCycleControls.Name = "flowCycleControls";
        flowCycleControls.Padding = new Padding(0);
        flowCycleControls.Size = new System.Drawing.Size(1016, 33);
        flowCycleControls.TabIndex = 0;
        flowCycleControls.WrapContents = false;
        flowCycleControls.Controls.Add(btnCyclePlay);
        flowCycleControls.Controls.Add(btnCycleStep);
        flowCycleControls.Controls.Add(btnCycleStop);
        flowCycleControls.Controls.Add(chkCycleRedeDisponivel);
        flowCycleControls.TabStop = false;
        //
        // btnCyclePlay
        //
        btnCyclePlay.AutoSize = true;
        btnCyclePlay.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCyclePlay.Margin = new Padding(0, 0, 8, 0);
        btnCyclePlay.Name = "btnCyclePlay";
        btnCyclePlay.Size = new System.Drawing.Size(68, 25);
        btnCyclePlay.TabIndex = 0;
        btnCyclePlay.Text = "Executar";
        btnCyclePlay.UseVisualStyleBackColor = true;
        btnCyclePlay.Click += btnCyclePlay_Click;
        //
        // btnCycleStep
        //
        btnCycleStep.AutoSize = true;
        btnCycleStep.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCycleStep.Margin = new Padding(0, 0, 8, 0);
        btnCycleStep.Name = "btnCycleStep";
        btnCycleStep.Size = new System.Drawing.Size(64, 25);
        btnCycleStep.TabIndex = 1;
        btnCycleStep.Text = "Avançar";
        btnCycleStep.UseVisualStyleBackColor = true;
        btnCycleStep.Click += btnCycleStep_Click;
        //
        // btnCycleStop
        //
        btnCycleStop.AutoSize = true;
        btnCycleStop.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCycleStop.Enabled = false;
        btnCycleStop.Margin = new Padding(0, 0, 16, 0);
        btnCycleStop.Name = "btnCycleStop";
        btnCycleStop.Size = new System.Drawing.Size(47, 25);
        btnCycleStop.TabIndex = 2;
        btnCycleStop.Text = "Parar";
        btnCycleStop.UseVisualStyleBackColor = true;
        btnCycleStop.Click += btnCycleStop_Click;
        //
        // chkCycleRedeDisponivel
        //
        chkCycleRedeDisponivel.AutoSize = true;
        chkCycleRedeDisponivel.Checked = true;
        chkCycleRedeDisponivel.CheckState = CheckState.Checked;
        chkCycleRedeDisponivel.Margin = new Padding(0, 4, 0, 0);
        chkCycleRedeDisponivel.Name = "chkCycleRedeDisponivel";
        chkCycleRedeDisponivel.Size = new System.Drawing.Size(120, 19);
        chkCycleRedeDisponivel.TabIndex = 3;
        chkCycleRedeDisponivel.Text = "Rede disponível";
        chkCycleRedeDisponivel.UseVisualStyleBackColor = true;
        chkCycleRedeDisponivel.CheckedChanged += chkCycleRedeDisponivel_CheckedChanged;
        //
        // flpCycleButtons
        //
        flpCycleButtons.AutoSize = true;
        flpCycleButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        flpCycleButtons.Dock = DockStyle.Fill;
        flpCycleButtons.FlowDirection = FlowDirection.TopDown;
        flpCycleButtons.Location = new System.Drawing.Point(936, 0);
        flpCycleButtons.Margin = new Padding(8, 0, 0, 0);
        flpCycleButtons.Name = "flpCycleButtons";
        flpCycleButtons.Padding = new Padding(0);
        flpCycleButtons.Size = new System.Drawing.Size(80, 200);
        flpCycleButtons.TabIndex = 1;
        flpCycleButtons.WrapContents = false;
        flpCycleButtons.Controls.Add(btnCycleUp);
        flpCycleButtons.Controls.Add(btnCycleDown);
        //
        // btnCycleUp
        //
        btnCycleUp.AutoSize = true;
        btnCycleUp.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCycleUp.Margin = new Padding(0, 0, 0, 8);
        btnCycleUp.Name = "btnCycleUp";
        btnCycleUp.Size = new System.Drawing.Size(45, 25);
        btnCycleUp.TabIndex = 0;
        btnCycleUp.Text = "Subir";
        btnCycleUp.UseVisualStyleBackColor = true;
        btnCycleUp.Click += btnCycleUp_Click;
        //
        // btnCycleDown
        //
        btnCycleDown.AutoSize = true;
        btnCycleDown.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCycleDown.Margin = new Padding(0);
        btnCycleDown.Name = "btnCycleDown";
        btnCycleDown.Size = new System.Drawing.Size(55, 25);
        btnCycleDown.TabIndex = 1;
        btnCycleDown.Text = "Descer";
        btnCycleDown.UseVisualStyleBackColor = true;
        btnCycleDown.Click += btnCycleDown_Click;
        //
        // flowCycleItems
        //
        flowCycleItems.AutoScroll = true;
        flowCycleItems.BackColor = System.Drawing.SystemColors.ControlLightLight;
        flowCycleItems.Dock = DockStyle.Fill;
        flowCycleItems.Location = new System.Drawing.Point(0, 241);
        flowCycleItems.Margin = new Padding(0);
        flowCycleItems.Name = "flowCycleItems";
        flowCycleItems.Padding = new Padding(0, 0, 0, 8);
        flowCycleItems.Size = new System.Drawing.Size(1016, 363);
        flowCycleItems.TabIndex = 1;
        //
        // tpCiclo
        //
        tpCiclo.Controls.Add(tlpCycle);
        tpCiclo.Location = new System.Drawing.Point(4, 24);
        tpCiclo.Margin = new Padding(8);
        tpCiclo.Name = "tpCiclo";
        tpCiclo.Padding = new Padding(8);
        tpCiclo.Size = new System.Drawing.Size(1032, 620);
        tpCiclo.TabIndex = 4;
        tpCiclo.Text = "Ciclo";
        tpCiclo.UseVisualStyleBackColor = true;
        //
        // tpAvancado
        //
        tpAvancado.Controls.Add(tlpAvancado);
        tpAvancado.Location = new System.Drawing.Point(4, 24);
        tpAvancado.Margin = new Padding(8);
        tpAvancado.Name = "tpAvancado";
        tpAvancado.Padding = new Padding(8);
        tpAvancado.Size = new System.Drawing.Size(1032, 620);
        tpAvancado.TabIndex = 5;
        tpAvancado.Text = "Avançado";
        tpAvancado.UseVisualStyleBackColor = true;
        //
        // tlpAvancado
        //
        tlpAvancado.ColumnCount = 2;
        tlpAvancado.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpAvancado.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpAvancado.Controls.Add(lblNomeAmigavel, 0, 0);
        tlpAvancado.Controls.Add(txtNomeAmigavel, 1, 0);
        tlpAvancado.Controls.Add(lblWindowTitle, 0, 1);
        tlpAvancado.Controls.Add(txtWindowTitle, 1, 1);
        tlpAvancado.Controls.Add(chkAlwaysOnTop, 0, 2);
        tlpAvancado.SetColumnSpan(chkAlwaysOnTop, 2);
        tlpAvancado.Controls.Add(chkWatchdogEnabled, 0, 3);
        tlpAvancado.SetColumnSpan(chkWatchdogEnabled, 2);
        tlpAvancado.Controls.Add(lblWatchdogGrace, 0, 4);
        tlpAvancado.Controls.Add(nudWatchdogGrace, 1, 4);
        tlpAvancado.Controls.Add(lblHealthCheckType, 0, 5);
        tlpAvancado.Controls.Add(cmbHealthCheckType, 1, 5);
        tlpAvancado.Controls.Add(lblHealthCheckUrl, 0, 6);
        tlpAvancado.Controls.Add(txtHealthCheckUrl, 1, 6);
        tlpAvancado.Controls.Add(lblHealthCheckDomSelector, 0, 7);
        tlpAvancado.Controls.Add(txtHealthCheckDomSelector, 1, 7);
        tlpAvancado.Controls.Add(lblHealthCheckContainsText, 0, 8);
        tlpAvancado.Controls.Add(txtHealthCheckContainsText, 1, 8);
        tlpAvancado.Controls.Add(lblHealthCheckInterval, 0, 9);
        tlpAvancado.Controls.Add(nudHealthCheckInterval, 1, 9);
        tlpAvancado.Controls.Add(lblHealthCheckTimeout, 0, 10);
        tlpAvancado.Controls.Add(nudHealthCheckTimeout, 1, 10);
        tlpAvancado.Controls.Add(lblEnvVars, 0, 11);
        tlpAvancado.SetColumnSpan(lblEnvVars, 2);
        tlpAvancado.Controls.Add(txtEnvVars, 0, 12);
        tlpAvancado.SetColumnSpan(txtEnvVars, 2);
        tlpAvancado.Dock = DockStyle.Fill;
        tlpAvancado.Location = new System.Drawing.Point(8, 8);
        tlpAvancado.Margin = new Padding(0);
        tlpAvancado.Name = "tlpAvancado";
        tlpAvancado.Padding = new Padding(0, 0, 0, 8);
        tlpAvancado.RowCount = 13;
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0 Nome
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1 Window Title
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2 AlwaysOnTop
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 3 Watchdog
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4 Grace
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5 HC Type
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 6 HC URL
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 7 HC DOM
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 8 HC Text
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 9 HC Interval
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 10 HC Timeout
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 11 EnvVars label
        tlpAvancado.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // 12 EnvVars text
        tlpAvancado.Size = new System.Drawing.Size(1016, 604);
        tlpAvancado.TabIndex = 0;
        //
        // lblNomeAmigavel
        //
        lblNomeAmigavel.AutoSize = true;
        lblNomeAmigavel.Margin = new Padding(0, 0, 8, 8);
        lblNomeAmigavel.Name = "lblNomeAmigavel";
        lblNomeAmigavel.Size = new System.Drawing.Size(83, 15);
        lblNomeAmigavel.TabIndex = 0;
        lblNomeAmigavel.Text = "Nome amigável";
        //
        // txtNomeAmigavel
        //
        txtNomeAmigavel.Dock = DockStyle.Fill;
        txtNomeAmigavel.Margin = new Padding(0, 0, 0, 8);
        txtNomeAmigavel.Name = "txtNomeAmigavel";
        txtNomeAmigavel.Size = new System.Drawing.Size(900, 23);
        txtNomeAmigavel.TabIndex = 1;
        txtNomeAmigavel.PlaceholderText = "Nome opcional para exibição";
        //
        // lblWindowTitle
        //
        lblWindowTitle.AutoSize = true;
        lblWindowTitle.Margin = new Padding(0, 0, 8, 8);
        lblWindowTitle.Name = "lblWindowTitle";
        lblWindowTitle.Size = new System.Drawing.Size(100, 15);
        lblWindowTitle.TabIndex = 2;
        lblWindowTitle.Text = "Título da janela";
        //
        // txtWindowTitle
        //
        txtWindowTitle.Dock = DockStyle.Fill;
        txtWindowTitle.Margin = new Padding(0, 0, 0, 8);
        txtWindowTitle.Name = "txtWindowTitle";
        txtWindowTitle.Size = new System.Drawing.Size(900, 23);
        txtWindowTitle.TabIndex = 3;
        txtWindowTitle.PlaceholderText = "Nome lógico da janela (opcional)";
        //
        // chkAlwaysOnTop
        //
        chkAlwaysOnTop.AutoSize = true;
        chkAlwaysOnTop.Margin = new Padding(0, 4, 0, 8);
        chkAlwaysOnTop.Name = "chkAlwaysOnTop";
        chkAlwaysOnTop.Size = new System.Drawing.Size(200, 19);
        chkAlwaysOnTop.TabIndex = 4;
        chkAlwaysOnTop.Text = "Manter janela sempre no topo";
        chkAlwaysOnTop.UseVisualStyleBackColor = true;
        //
        // chkWatchdogEnabled
        //
        chkWatchdogEnabled.AutoSize = true;
        chkWatchdogEnabled.Margin = new Padding(0, 8, 0, 8);
        chkWatchdogEnabled.Name = "chkWatchdogEnabled";
        chkWatchdogEnabled.Size = new System.Drawing.Size(200, 19);
        chkWatchdogEnabled.TabIndex = 5;
        chkWatchdogEnabled.Text = "Supervisão (Watchdog) ativada";
        chkWatchdogEnabled.Checked = true;
        chkWatchdogEnabled.UseVisualStyleBackColor = true;
        //
        // lblWatchdogGrace
        //
        lblWatchdogGrace.AutoSize = true;
        lblWatchdogGrace.Margin = new Padding(0, 0, 8, 8);
        lblWatchdogGrace.Name = "lblWatchdogGrace";
        lblWatchdogGrace.Size = new System.Drawing.Size(160, 15);
        lblWatchdogGrace.TabIndex = 6;
        lblWatchdogGrace.Text = "Carência pós-reinício (seg)";
        //
        // nudWatchdogGrace
        //
        nudWatchdogGrace.Margin = new Padding(0, 0, 8, 8);
        nudWatchdogGrace.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
        nudWatchdogGrace.Minimum = new decimal(new int[] { 0, 0, 0, 0 });
        nudWatchdogGrace.Name = "nudWatchdogGrace";
        nudWatchdogGrace.Size = new System.Drawing.Size(160, 23);
        nudWatchdogGrace.TabIndex = 7;
        nudWatchdogGrace.Value = new decimal(new int[] { 15, 0, 0, 0 });
        //
        // lblHealthCheckType
        //
        lblHealthCheckType.AutoSize = true;
        lblHealthCheckType.Margin = new Padding(0, 0, 8, 8);
        lblHealthCheckType.Name = "lblHealthCheckType";
        lblHealthCheckType.Size = new System.Drawing.Size(100, 15);
        lblHealthCheckType.TabIndex = 8;
        lblHealthCheckType.Text = "Health Check";
        //
        // cmbHealthCheckType
        //
        cmbHealthCheckType.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbHealthCheckType.FormattingEnabled = true;
        cmbHealthCheckType.Items.AddRange(new object[] { "Nenhum", "Ping (HTTP)", "DOM (conteúdo)" });
        cmbHealthCheckType.Margin = new Padding(0, 0, 0, 8);
        cmbHealthCheckType.Name = "cmbHealthCheckType";
        cmbHealthCheckType.Size = new System.Drawing.Size(200, 23);
        cmbHealthCheckType.TabIndex = 9;
        //
        // lblHealthCheckUrl
        //
        lblHealthCheckUrl.AutoSize = true;
        lblHealthCheckUrl.Margin = new Padding(0, 0, 8, 8);
        lblHealthCheckUrl.Name = "lblHealthCheckUrl";
        lblHealthCheckUrl.Size = new System.Drawing.Size(80, 15);
        lblHealthCheckUrl.TabIndex = 10;
        lblHealthCheckUrl.Text = "URL de verificação";
        //
        // txtHealthCheckUrl
        //
        txtHealthCheckUrl.Dock = DockStyle.Fill;
        txtHealthCheckUrl.Margin = new Padding(0, 0, 0, 8);
        txtHealthCheckUrl.Name = "txtHealthCheckUrl";
        txtHealthCheckUrl.Size = new System.Drawing.Size(900, 23);
        txtHealthCheckUrl.TabIndex = 11;
        txtHealthCheckUrl.PlaceholderText = "https://exemplo.com/health";
        //
        // lblHealthCheckDomSelector
        //
        lblHealthCheckDomSelector.AutoSize = true;
        lblHealthCheckDomSelector.Margin = new Padding(0, 0, 8, 8);
        lblHealthCheckDomSelector.Name = "lblHealthCheckDomSelector";
        lblHealthCheckDomSelector.Size = new System.Drawing.Size(80, 15);
        lblHealthCheckDomSelector.TabIndex = 12;
        lblHealthCheckDomSelector.Text = "Seletor DOM";
        //
        // txtHealthCheckDomSelector
        //
        txtHealthCheckDomSelector.Dock = DockStyle.Fill;
        txtHealthCheckDomSelector.Margin = new Padding(0, 0, 0, 8);
        txtHealthCheckDomSelector.Name = "txtHealthCheckDomSelector";
        txtHealthCheckDomSelector.Size = new System.Drawing.Size(900, 23);
        txtHealthCheckDomSelector.TabIndex = 13;
        txtHealthCheckDomSelector.PlaceholderText = "#status, .content, div.main";
        //
        // lblHealthCheckContainsText
        //
        lblHealthCheckContainsText.AutoSize = true;
        lblHealthCheckContainsText.Margin = new Padding(0, 0, 8, 8);
        lblHealthCheckContainsText.Name = "lblHealthCheckContainsText";
        lblHealthCheckContainsText.Size = new System.Drawing.Size(80, 15);
        lblHealthCheckContainsText.TabIndex = 14;
        lblHealthCheckContainsText.Text = "Texto esperado";
        //
        // txtHealthCheckContainsText
        //
        txtHealthCheckContainsText.Dock = DockStyle.Fill;
        txtHealthCheckContainsText.Margin = new Padding(0, 0, 0, 8);
        txtHealthCheckContainsText.Name = "txtHealthCheckContainsText";
        txtHealthCheckContainsText.Size = new System.Drawing.Size(900, 23);
        txtHealthCheckContainsText.TabIndex = 15;
        txtHealthCheckContainsText.PlaceholderText = "Texto que deve estar presente na página";
        //
        // lblHealthCheckInterval
        //
        lblHealthCheckInterval.AutoSize = true;
        lblHealthCheckInterval.Margin = new Padding(0, 0, 8, 8);
        lblHealthCheckInterval.Name = "lblHealthCheckInterval";
        lblHealthCheckInterval.Size = new System.Drawing.Size(100, 15);
        lblHealthCheckInterval.TabIndex = 16;
        lblHealthCheckInterval.Text = "Intervalo (seg)";
        //
        // nudHealthCheckInterval
        //
        nudHealthCheckInterval.Margin = new Padding(0, 0, 8, 8);
        nudHealthCheckInterval.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
        nudHealthCheckInterval.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
        nudHealthCheckInterval.Name = "nudHealthCheckInterval";
        nudHealthCheckInterval.Size = new System.Drawing.Size(160, 23);
        nudHealthCheckInterval.TabIndex = 17;
        nudHealthCheckInterval.Value = new decimal(new int[] { 60, 0, 0, 0 });
        //
        // lblHealthCheckTimeout
        //
        lblHealthCheckTimeout.AutoSize = true;
        lblHealthCheckTimeout.Margin = new Padding(0, 0, 8, 8);
        lblHealthCheckTimeout.Name = "lblHealthCheckTimeout";
        lblHealthCheckTimeout.Size = new System.Drawing.Size(100, 15);
        lblHealthCheckTimeout.TabIndex = 18;
        lblHealthCheckTimeout.Text = "Timeout (seg)";
        //
        // nudHealthCheckTimeout
        //
        nudHealthCheckTimeout.Margin = new Padding(0, 0, 8, 8);
        nudHealthCheckTimeout.Maximum = new decimal(new int[] { 120, 0, 0, 0 });
        nudHealthCheckTimeout.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        nudHealthCheckTimeout.Name = "nudHealthCheckTimeout";
        nudHealthCheckTimeout.Size = new System.Drawing.Size(160, 23);
        nudHealthCheckTimeout.TabIndex = 19;
        nudHealthCheckTimeout.Value = new decimal(new int[] { 10, 0, 0, 0 });
        //
        // lblEnvVars
        //
        lblEnvVars.AutoSize = true;
        lblEnvVars.Margin = new Padding(0, 8, 0, 4);
        lblEnvVars.Name = "lblEnvVars";
        lblEnvVars.Size = new System.Drawing.Size(260, 15);
        lblEnvVars.TabIndex = 20;
        lblEnvVars.Text = "Variáveis de ambiente (CHAVE=VALOR por linha)";
        //
        // txtEnvVars
        //
        txtEnvVars.Dock = DockStyle.Fill;
        txtEnvVars.Margin = new Padding(0);
        txtEnvVars.Multiline = true;
        txtEnvVars.Name = "txtEnvVars";
        txtEnvVars.ScrollBars = ScrollBars.Vertical;
        txtEnvVars.Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9F);
        txtEnvVars.Size = new System.Drawing.Size(1016, 400);
        txtEnvVars.TabIndex = 21;
        txtEnvVars.PlaceholderText = "DISPLAY=:0\nLANG=pt_BR.UTF-8";
        //
        // painelRodape
        //
        painelRodape.AutoSize = true;
        painelRodape.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelRodape.Dock = DockStyle.Bottom;
        painelRodape.FlowDirection = FlowDirection.RightToLeft;
        painelRodape.Location = new System.Drawing.Point(0, 648);
        painelRodape.Margin = new Padding(8);
        painelRodape.Name = "painelRodape";
        painelRodape.Padding = new Padding(12);
        painelRodape.Size = new System.Drawing.Size(1040, 64);
        painelRodape.TabIndex = 1;
        painelRodape.WrapContents = false;
        painelRodape.Controls.Add(btnSalvar);
        painelRodape.Controls.Add(btnCancelar);
        //
        // btnSalvar
        //
        btnSalvar.AutoSize = true;
        btnSalvar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnSalvar.Margin = new Padding(6, 3, 0, 3);
        btnSalvar.Name = "btnSalvar";
        btnSalvar.Size = new System.Drawing.Size(59, 25);
        btnSalvar.TabIndex = 0;
        btnSalvar.Text = "Salvar";
        btnSalvar.UseVisualStyleBackColor = true;
        btnSalvar.Click += btnSalvar_Click;
        editorToolTip.SetToolTip(btnSalvar, "Salvar configuração (Ctrl+S)");
        //
        // btnCancelar
        //
        btnCancelar.AutoSize = true;
        btnCancelar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCancelar.Margin = new Padding(6, 3, 0, 3);
        btnCancelar.Name = "btnCancelar";
        btnCancelar.Size = new System.Drawing.Size(75, 25);
        btnCancelar.TabIndex = 1;
        btnCancelar.Text = "Cancelar";
        btnCancelar.UseVisualStyleBackColor = true;
        btnCancelar.Click += btnCancelar_Click;
        editorToolTip.SetToolTip(btnCancelar, "Descartar alterações e fechar (Esc)");
        //
        // errorProvider
        //
        errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        errorProvider.ContainerControl = this;
        //
        // AppEditorForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1040, 712);
        Controls.Add(tabEditor);
        Controls.Add(painelRodape);
        MainMenuStrip = null;
        Margin = new Padding(8);
        MinimumSize = new System.Drawing.Size(960, 640);
        Name = "AppEditorForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Editor de Programa";
        tabEditor.ResumeLayout(false);
        tpGeral.ResumeLayout(false);
        tabAplicativos.ResumeLayout(false);
        tpJanela.ResumeLayout(false);
        tabSites.ResumeLayout(false);
        pnlBrowserPanel.ResumeLayout(false);
        tlpCycleList.ResumeLayout(false);
        tlpCycleList.PerformLayout();
        tlpCycle.ResumeLayout(false);
        tlpCycle.PerformLayout();
        flowCycleControls.ResumeLayout(false);
        flowCycleControls.PerformLayout();
        flpCycleButtons.ResumeLayout(false);
        flpCycleButtons.PerformLayout();
        flowCycleItems.ResumeLayout(false);
        tpCiclo.ResumeLayout(false);
        tlpAvancado.ResumeLayout(false);
        tlpAvancado.PerformLayout();
        tpAvancado.ResumeLayout(false);
        tpAvancado.PerformLayout();
        ((ISupportInitialize)nudWatchdogGrace).EndInit();
        ((ISupportInitialize)nudHealthCheckInterval).EndInit();
        ((ISupportInitialize)nudHealthCheckTimeout).EndInit();
        painelRodape.ResumeLayout(false);
        painelRodape.PerformLayout();
        tlpGeral.ResumeLayout(false);
        tlpGeral.PerformLayout();
        tlpExecutavel.ResumeLayout(false);
        tlpExecutavel.PerformLayout();
        flowAppType.ResumeLayout(false);
        flowAppType.PerformLayout();
        tlpJanela.ResumeLayout(false);
        tlpJanela.PerformLayout();
        tlpMonitorPreview.ResumeLayout(false);
        tlpMonitorPreview.PerformLayout();
        ((ISupportInitialize)nudJanelaX).EndInit();
        ((ISupportInitialize)nudJanelaY).EndInit();
        ((ISupportInitialize)nudJanelaLargura).EndInit();
        ((ISupportInitialize)nudJanelaAltura).EndInit();
        ((ISupportInitialize)dgvCycle).EndInit();
        ((ISupportInitialize)bsCycle).EndInit();
        ((ISupportInitialize)errorProvider).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
