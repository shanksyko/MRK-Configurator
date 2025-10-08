#nullable enable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

partial class WhitelistTab
{
    private IContainer? components = null;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal ListBox lstHosts = null!;
    internal TextBox txtHostEntrada = null!;
    internal Button btnAdicionar = null!;
    internal Button btnRemover = null!;
    internal Button btnNormalizar = null!;
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
        lstHosts = new ListBox();
        var painelComandos = new FlowLayoutPanel();
        txtHostEntrada = new TextBox();
        btnAdicionar = new Button();
        btnRemover = new Button();
        btnNormalizar = new Button();
        errorProvider = new ErrorProvider(components);
        SuspendLayout();
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 1;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.Controls.Add(lstHosts, 0, 0);
        layoutPrincipal.Controls.Add(painelComandos, 0, 1);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 0);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 2;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.Size = new System.Drawing.Size(520, 420);
        layoutPrincipal.TabIndex = 0;
        //
        // lstHosts
        //
        lstHosts.Dock = DockStyle.Fill;
        lstHosts.FormattingEnabled = true;
        lstHosts.ItemHeight = 15;
        lstHosts.Location = new System.Drawing.Point(11, 11);
        lstHosts.Margin = new Padding(3, 3, 3, 8);
        lstHosts.Name = "lstHosts";
        lstHosts.Size = new System.Drawing.Size(498, 359);
        lstHosts.TabIndex = 0;
        //
        // painelComandos
        //
        painelComandos.AutoSize = true;
        painelComandos.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelComandos.Dock = DockStyle.Fill;
        painelComandos.FlowDirection = FlowDirection.LeftToRight;
        painelComandos.Location = new System.Drawing.Point(11, 378);
        painelComandos.Margin = new Padding(3, 0, 3, 0);
        painelComandos.Name = "painelComandos";
        painelComandos.Size = new System.Drawing.Size(498, 34);
        painelComandos.TabIndex = 1;
        painelComandos.WrapContents = false;
        painelComandos.Controls.Add(txtHostEntrada);
        painelComandos.Controls.Add(btnAdicionar);
        painelComandos.Controls.Add(btnRemover);
        painelComandos.Controls.Add(btnNormalizar);
        //
        // txtHostEntrada
        //
        txtHostEntrada.Margin = new Padding(0, 0, 8, 0);
        txtHostEntrada.Name = "txtHostEntrada";
        txtHostEntrada.Size = new System.Drawing.Size(220, 23);
        txtHostEntrada.TabIndex = 0;
        //
        // btnAdicionar
        //
        btnAdicionar.AutoSize = true;
        btnAdicionar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnAdicionar.Margin = new Padding(0, 0, 8, 0);
        btnAdicionar.Name = "btnAdicionar";
        btnAdicionar.Size = new System.Drawing.Size(84, 25);
        btnAdicionar.TabIndex = 1;
        btnAdicionar.Text = "Adicionar";
        btnAdicionar.UseVisualStyleBackColor = true;
        btnAdicionar.Click += btnAdicionar_Click;
        //
        // btnRemover
        //
        btnRemover.AutoSize = true;
        btnRemover.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnRemover.Margin = new Padding(0, 0, 8, 0);
        btnRemover.Name = "btnRemover";
        btnRemover.Size = new System.Drawing.Size(72, 25);
        btnRemover.TabIndex = 2;
        btnRemover.Text = "Remover";
        btnRemover.UseVisualStyleBackColor = true;
        btnRemover.Click += btnRemover_Click;
        //
        // btnNormalizar
        //
        btnNormalizar.AutoSize = true;
        btnNormalizar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnNormalizar.Margin = new Padding(0);
        btnNormalizar.Name = "btnNormalizar";
        btnNormalizar.Size = new System.Drawing.Size(89, 25);
        btnNormalizar.TabIndex = 3;
        btnNormalizar.Text = "Normalizar";
        btnNormalizar.UseVisualStyleBackColor = true;
        btnNormalizar.Click += btnNormalizar_Click;
        //
        // errorProvider
        //
        errorProvider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        errorProvider.ContainerControl = this;
        //
        // WhitelistTab
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(layoutPrincipal);
        Name = "WhitelistTab";
        Size = new System.Drawing.Size(520, 420);
        ResumeLayout(false);
    }

    #endregion
}
