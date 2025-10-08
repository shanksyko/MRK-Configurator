#nullable enable
using System.ComponentModel;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.App.Forms.Controls.Apps;
using Mieruka.App.Ui.PreviewBindings;

namespace Mieruka.App.Forms;

partial class AppEditorForm
{
    private IContainer? components = null;
    internal TabControl tabEditor = null!;
    internal TabPage tpGeral = null!;
    internal TabPage tpAplicativos = null!;
    internal TabPage tpJanela = null!;
    internal TabPage tpSites = null!;
    internal TabPage tpCiclo = null!;
    internal TabPage tpAvancado = null!;
    internal SitesEditorControl sitesEditorControl = null!;
    internal AppsTab appsTabControl = null!;
    internal Button btnSalvar = null!;
    internal Button btnCancelar = null!;
    internal Button btnTestarJanela = null!;
    internal Button btnTestReal = null!;
    internal Button btnCyclePlay = null!;
    internal Button btnCycleStep = null!;
    internal Button btnCycleStop = null!;
    internal TextBox txtId = null!;
    internal TextBox txtExecutavel = null!;
    internal TextBox txtArgumentos = null!;
    internal TextBox txtCmdPreviewExe = null!;
    internal RadioButton rbExe = null!;
    internal RadioButton rbBrowser = null!;
    internal GroupBox grpInstalledApps = null!;
    internal ListView lvApps = null!;
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
        tpAplicativos = new TabPage();
        tpJanela = new TabPage();
        tpSites = new TabPage();
        tpCiclo = new TabPage();
        tpAvancado = new TabPage();
        sitesEditorControl = new SitesEditorControl();
        appsTabControl = new AppsTab();
        var painelRodape = new FlowLayoutPanel();
        btnSalvar = new Button();
        btnCancelar = new Button();
        btnTestarJanela = new Button();
        btnTestReal = new Button();
        var tlpGeral = new TableLayoutPanel();
        var lblId = new Label();
        txtId = new TextBox();
        var lblExecutavel = new Label();
        txtExecutavel = new TextBox();
        var lblArgumentos = new Label();
        txtArgumentos = new TextBox();
        var lblCmdPreviewExe = new Label();
        txtCmdPreviewExe = new TextBox();
        chkAutoStart = new CheckBox();
        rbExe = new RadioButton();
        rbBrowser = new RadioButton();
        grpInstalledApps = new GroupBox();
        lvApps = new ListView();
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
        var lblAvancado = new Label();
        errorProvider = new ErrorProvider(components);
        cycleToolTip = new ToolTip(components);
        grpInstalledApps.SuspendLayout();
        tlpExecutavel.SuspendLayout();
        flowAppType.SuspendLayout();
        ((ISupportInitialize)bsCycle).BeginInit();
        ((ISupportInitialize)dgvCycle).BeginInit();
        tabEditor.SuspendLayout();
        tpGeral.SuspendLayout();
        tpAplicativos.SuspendLayout();
        tpJanela.SuspendLayout();
        tpSites.SuspendLayout();
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
        tabEditor.Controls.Add(tpAplicativos);
        tabEditor.Controls.Add(tpJanela);
        tabEditor.Controls.Add(tpSites);
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
        // tpAplicativos
        //
        tpAplicativos.Controls.Add(appsTabControl);
        tpAplicativos.Location = new System.Drawing.Point(4, 24);
        tpAplicativos.Margin = new Padding(8);
        tpAplicativos.Name = "tpAplicativos";
        tpAplicativos.Padding = new Padding(8);
        tpAplicativos.Size = new System.Drawing.Size(1032, 620);
        tpAplicativos.TabIndex = 1;
        tpAplicativos.Text = "Aplicativos";
        tpAplicativos.UseVisualStyleBackColor = true;
        //
        // appsTabControl
        //
        appsTabControl.Dock = DockStyle.Fill;
        appsTabControl.Margin = new Padding(0);
        appsTabControl.Name = "appsTabControl";
        appsTabControl.TabIndex = 0;
        //
        // tlpGeral
        //
        tlpGeral.ColumnCount = 2;
        tlpGeral.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpGeral.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tlpGeral.Controls.Add(lblId, 0, 0);
        tlpGeral.Controls.Add(txtId, 1, 0);
        tlpGeral.Controls.Add(flowAppType, 0, 1);
        tlpGeral.Controls.Add(grpInstalledApps, 0, 2);
        tlpGeral.Controls.Add(lblExecutavel, 0, 3);
        tlpGeral.Controls.Add(tlpExecutavel, 1, 3);
        tlpGeral.Controls.Add(lblArgumentos, 0, 4);
        tlpGeral.Controls.Add(txtArgumentos, 1, 4);
        tlpGeral.Controls.Add(lblCmdPreviewExe, 0, 5);
        tlpGeral.Controls.Add(txtCmdPreviewExe, 1, 5);
        tlpGeral.Controls.Add(chkAutoStart, 1, 6);
        tlpGeral.SetColumnSpan(flowAppType, 2);
        tlpGeral.SetColumnSpan(grpInstalledApps, 2);
        tlpGeral.Dock = DockStyle.Fill;
        tlpGeral.Location = new System.Drawing.Point(8, 8);
        tlpGeral.Margin = new Padding(0);
        tlpGeral.Name = "tlpGeral";
        tlpGeral.Padding = new Padding(0, 0, 0, 8);
        tlpGeral.RowCount = 7;
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlpGeral.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
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
        // rbExe
        //
        rbExe.AutoSize = true;
        rbExe.Checked = true;
        rbExe.Margin = new Padding(0, 0, 16, 0);
        rbExe.Name = "rbExe";
        rbExe.Size = new System.Drawing.Size(83, 19);
        rbExe.TabIndex = 0;
        rbExe.TabStop = true;
        rbExe.Text = "Executável";
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
        // grpInstalledApps
        //
        grpInstalledApps.Controls.Add(lvApps);
        grpInstalledApps.Dock = DockStyle.Fill;
        grpInstalledApps.Location = new System.Drawing.Point(0, 66);
        grpInstalledApps.Margin = new Padding(0, 0, 0, 8);
        grpInstalledApps.Name = "grpInstalledApps";
        grpInstalledApps.Padding = new Padding(8);
        grpInstalledApps.Size = new System.Drawing.Size(1016, 209);
        grpInstalledApps.TabIndex = 3;
        grpInstalledApps.TabStop = false;
        grpInstalledApps.Text = "Aplicativos instalados";
        //
        // lvApps
        //
        lvApps.BackColor = System.Drawing.SystemColors.Window;
        lvApps.Dock = DockStyle.Fill;
        lvApps.ForeColor = System.Drawing.SystemColors.WindowText;
        lvApps.FullRowSelect = true;
        lvApps.HideSelection = false;
        lvApps.Location = new System.Drawing.Point(8, 24);
        lvApps.Margin = new Padding(0);
        lvApps.Name = "lvApps";
        lvApps.OwnerDraw = false;
        lvApps.Size = new System.Drawing.Size(1000, 177);
        lvApps.TabIndex = 0;
        lvApps.UseCompatibleStateImageBehavior = false;
        lvApps.View = View.Details;
        //
        // lblExecutavel
        //
        lblExecutavel.AutoSize = true;
        lblExecutavel.Margin = new Padding(0, 0, 8, 8);
        lblExecutavel.Name = "lblExecutavel";
        lblExecutavel.Size = new System.Drawing.Size(66, 15);
        lblExecutavel.TabIndex = 4;
        lblExecutavel.Text = "Executável";
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
        tlpExecutavel.TabIndex = 5;
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
        //
        // lblArgumentos
        //
        lblArgumentos.AutoSize = true;
        lblArgumentos.Margin = new Padding(0, 0, 8, 8);
        lblArgumentos.Name = "lblArgumentos";
        lblArgumentos.Size = new System.Drawing.Size(73, 15);
        lblArgumentos.TabIndex = 6;
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
        txtArgumentos.TabIndex = 7;
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
        tpJanela.Controls.Add(tlpMonitorPreview);
        tpJanela.Controls.Add(tlpJanela);
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
        monitorPreviewDisplay.Size = new System.Drawing.Size(424, 552);
        monitorPreviewDisplay.TabIndex = 6;
        monitorPreviewDisplay.TabStop = false;
        //
        // lblMonitorCoordinates
        //
        lblMonitorCoordinates.AutoSize = true;
        lblMonitorCoordinates.Dock = DockStyle.Fill;
        lblMonitorCoordinates.Location = new System.Drawing.Point(8, 568);
        lblMonitorCoordinates.Margin = new Padding(0);
        lblMonitorCoordinates.Name = "lblMonitorCoordinates";
        lblMonitorCoordinates.Padding = new Padding(0, 4, 0, 0);
        lblMonitorCoordinates.Size = new System.Drawing.Size(424, 28);
        lblMonitorCoordinates.TabIndex = 7;
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
        //
        // tpSites
        //
        tpSites.Controls.Add(sitesEditorControl);
        tpSites.Location = new System.Drawing.Point(4, 24);
        tpSites.Margin = new Padding(8);
        tpSites.Name = "tpSites";
        tpSites.Padding = new Padding(8);
        tpSites.Size = new System.Drawing.Size(1032, 620);
        tpSites.TabIndex = 3;
        tpSites.Text = "Sites";
        tpSites.UseVisualStyleBackColor = true;
        //
        // sitesEditorControl
        //
        sitesEditorControl.Dock = DockStyle.Fill;
        sitesEditorControl.Location = new System.Drawing.Point(8, 8);
        sitesEditorControl.Margin = new Padding(0);
        sitesEditorControl.Name = "sitesEditorControl";
        sitesEditorControl.Size = new System.Drawing.Size(1016, 604);
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
        tpAvancado.Controls.Add(lblAvancado);
        tpAvancado.Location = new System.Drawing.Point(4, 24);
        tpAvancado.Margin = new Padding(8);
        tpAvancado.Name = "tpAvancado";
        tpAvancado.Padding = new Padding(8);
        tpAvancado.Size = new System.Drawing.Size(1032, 620);
        tpAvancado.TabIndex = 5;
        tpAvancado.Text = "Avançado";
        tpAvancado.UseVisualStyleBackColor = true;
        //
        // lblAvancado
        //
        lblAvancado.AutoSize = true;
        lblAvancado.Dock = DockStyle.Top;
        lblAvancado.Margin = new Padding(0);
        lblAvancado.Name = "lblAvancado";
        lblAvancado.Padding = new Padding(0, 0, 0, 8);
        lblAvancado.Size = new System.Drawing.Size(305, 15);
        lblAvancado.TabIndex = 0;
        lblAvancado.Text = "Recursos avançados poderão ser ajustados futuramente.";
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
        tpAplicativos.ResumeLayout(false);
        tpJanela.ResumeLayout(false);
        tpSites.ResumeLayout(false);
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
        tpAvancado.ResumeLayout(false);
        tpAvancado.PerformLayout();
        painelRodape.ResumeLayout(false);
        painelRodape.PerformLayout();
        tlpGeral.ResumeLayout(false);
        tlpGeral.PerformLayout();
        grpInstalledApps.ResumeLayout(false);
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
