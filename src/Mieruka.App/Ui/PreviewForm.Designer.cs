#nullable enable
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Ui;

partial class PreviewForm
{
    private IContainer? components = null;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal FlowLayoutPanel painelControles = null!;
    internal ComboBox cmbMonitores = null!;
    internal Button btnIniciar = null!;
    internal Button btnParar = null!;
    internal Button btnCapturaGdi = null!;
    internal Button btnCapturaGpu = null!;
    internal PictureBox picPreview = null!;
    internal Label lblStatus = null!;

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
        painelControles = new FlowLayoutPanel();
        cmbMonitores = new ComboBox();
        btnIniciar = new Button();
        btnParar = new Button();
        btnCapturaGdi = new Button();
        btnCapturaGpu = new Button();
        picPreview = new PictureBox();
        lblStatus = new Label();
        ((ISupportInitialize)picPreview).BeginInit();
        SuspendLayout();
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 1;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.Controls.Add(painelControles, 0, 0);
        layoutPrincipal.Controls.Add(picPreview, 0, 1);
        layoutPrincipal.Controls.Add(lblStatus, 0, 2);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 0);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 3;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.Size = new System.Drawing.Size(960, 600);
        layoutPrincipal.TabIndex = 0;
        //
        // painelControles
        //
        painelControles.AutoSize = true;
        painelControles.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelControles.Dock = DockStyle.Fill;
        painelControles.FlowDirection = FlowDirection.LeftToRight;
        painelControles.Location = new System.Drawing.Point(11, 11);
        painelControles.Margin = new Padding(3, 3, 3, 8);
        painelControles.Name = "painelControles";
        painelControles.Size = new System.Drawing.Size(938, 35);
        painelControles.TabIndex = 0;
        painelControles.WrapContents = false;
        painelControles.Controls.Add(cmbMonitores);
        painelControles.Controls.Add(btnIniciar);
        painelControles.Controls.Add(btnParar);
        painelControles.Controls.Add(btnCapturaGdi);
        painelControles.Controls.Add(btnCapturaGpu);
        //
        // cmbMonitores
        //
        cmbMonitores.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbMonitores.FormattingEnabled = true;
        cmbMonitores.Margin = new Padding(0, 0, 8, 0);
        cmbMonitores.Name = "cmbMonitores";
        cmbMonitores.Size = new System.Drawing.Size(260, 23);
        cmbMonitores.TabIndex = 0;
        //
        // btnIniciar
        //
        btnIniciar.AutoSize = true;
        btnIniciar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnIniciar.Margin = new Padding(0, 0, 8, 0);
        btnIniciar.Name = "btnIniciar";
        btnIniciar.Size = new System.Drawing.Size(57, 25);
        btnIniciar.TabIndex = 1;
        btnIniciar.Text = "Iniciar";
        btnIniciar.UseVisualStyleBackColor = true;
        btnIniciar.Click += btnIniciar_Click;
        //
        // btnParar
        //
        btnParar.AutoSize = true;
        btnParar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnParar.Margin = new Padding(0, 0, 8, 0);
        btnParar.Name = "btnParar";
        btnParar.Size = new System.Drawing.Size(46, 25);
        btnParar.TabIndex = 2;
        btnParar.Text = "Parar";
        btnParar.UseVisualStyleBackColor = true;
        btnParar.Click += btnParar_Click;
        //
        // btnCapturaGdi
        //
        btnCapturaGdi.AutoSize = true;
        btnCapturaGdi.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCapturaGdi.Margin = new Padding(0, 0, 8, 0);
        btnCapturaGdi.Name = "btnCapturaGdi";
        btnCapturaGdi.Size = new System.Drawing.Size(97, 25);
        btnCapturaGdi.TabIndex = 3;
        btnCapturaGdi.Text = "Captura GDI";
        btnCapturaGdi.UseVisualStyleBackColor = true;
        btnCapturaGdi.Click += btnCapturaGdi_Click;
        //
        // btnCapturaGpu
        //
        btnCapturaGpu.AutoSize = true;
        btnCapturaGpu.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnCapturaGpu.Margin = new Padding(0);
        btnCapturaGpu.Name = "btnCapturaGpu";
        btnCapturaGpu.Size = new System.Drawing.Size(105, 25);
        btnCapturaGpu.TabIndex = 4;
        btnCapturaGpu.Text = "Captura GPU";
        btnCapturaGpu.UseVisualStyleBackColor = true;
        btnCapturaGpu.Click += btnCapturaGpu_Click;
        //
        // picPreview
        //
        picPreview.BackColor = System.Drawing.Color.Black;
        picPreview.Dock = DockStyle.Fill;
        picPreview.Location = new System.Drawing.Point(11, 57);
        picPreview.Margin = new Padding(3, 3, 3, 8);
        picPreview.Name = "picPreview";
        picPreview.Size = new System.Drawing.Size(938, 497);
        picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        picPreview.TabIndex = 1;
        picPreview.TabStop = false;
        //
        // lblStatus
        //
        lblStatus.AutoSize = true;
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Margin = new Padding(3, 0, 3, 3);
        lblStatus.Name = "lblStatus";
        lblStatus.Padding = new Padding(0, 4, 0, 0);
        lblStatus.Size = new System.Drawing.Size(938, 34);
        lblStatus.TabIndex = 2;
        lblStatus.Text = "Selecione um monitor e clique em Iniciar.";
        //
        // PreviewForm
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(960, 600);
        Controls.Add(layoutPrincipal);
        Margin = new Padding(8);
        MinimumSize = new System.Drawing.Size(800, 480);
        Name = "PreviewForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Preview de Monitores";
        ((ISupportInitialize)picPreview).EndInit();
        ResumeLayout(false);
    }

    #endregion
}
