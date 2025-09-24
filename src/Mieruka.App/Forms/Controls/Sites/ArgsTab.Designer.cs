#nullable enable annotations
#nullable disable warnings
using System.ComponentModel;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls.Sites;

partial class ArgsTab
{
    private IContainer? components = null;
    internal TableLayoutPanel layoutPrincipal = null!;
    internal CheckBox chkKiosk = null!;
    internal CheckBox chkAppMode = null!;
    internal CheckBox chkIncognito = null!;
    internal TextBox txtProxy = null!;
    internal TextBox txtBypass = null!;
    internal NumericUpDown nudTimeout = null!;
    internal NumericUpDown nudPostLoginDelay = null!;
    internal TextBox txtPreview = null!;

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
        var painelSwitches = new FlowLayoutPanel();
        chkKiosk = new CheckBox();
        chkAppMode = new CheckBox();
        chkIncognito = new CheckBox();
        var layoutProxy = new TableLayoutPanel();
        var lblProxy = new Label();
        txtProxy = new TextBox();
        var lblBypass = new Label();
        txtBypass = new TextBox();
        var painelTempos = new FlowLayoutPanel();
        var lblTimeout = new Label();
        nudTimeout = new NumericUpDown();
        var lblPostDelay = new Label();
        nudPostLoginDelay = new NumericUpDown();
        txtPreview = new TextBox();
        SuspendLayout();
        //
        // layoutPrincipal
        //
        layoutPrincipal.ColumnCount = 1;
        layoutPrincipal.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutPrincipal.Controls.Add(painelSwitches, 0, 0);
        layoutPrincipal.Controls.Add(layoutProxy, 0, 1);
        layoutPrincipal.Controls.Add(painelTempos, 0, 2);
        layoutPrincipal.Controls.Add(txtPreview, 0, 3);
        layoutPrincipal.Dock = DockStyle.Fill;
        layoutPrincipal.Location = new System.Drawing.Point(0, 0);
        layoutPrincipal.Margin = new Padding(8);
        layoutPrincipal.Name = "layoutPrincipal";
        layoutPrincipal.Padding = new Padding(8);
        layoutPrincipal.RowCount = 4;
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutPrincipal.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutPrincipal.Size = new System.Drawing.Size(520, 420);
        layoutPrincipal.TabIndex = 0;
        //
        // painelSwitches
        //
        painelSwitches.AutoSize = true;
        painelSwitches.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelSwitches.Dock = DockStyle.Fill;
        painelSwitches.FlowDirection = FlowDirection.LeftToRight;
        painelSwitches.Location = new System.Drawing.Point(11, 11);
        painelSwitches.Margin = new Padding(3, 3, 3, 8);
        painelSwitches.Name = "painelSwitches";
        painelSwitches.Size = new System.Drawing.Size(498, 27);
        painelSwitches.TabIndex = 0;
        painelSwitches.WrapContents = false;
        painelSwitches.Controls.Add(chkKiosk);
        painelSwitches.Controls.Add(chkAppMode);
        painelSwitches.Controls.Add(chkIncognito);
        //
        // chkKiosk
        //
        chkKiosk.AutoSize = true;
        chkKiosk.Margin = new Padding(0, 0, 16, 0);
        chkKiosk.Name = "chkKiosk";
        chkKiosk.Size = new System.Drawing.Size(60, 19);
        chkKiosk.TabIndex = 0;
        chkKiosk.Text = "kiosk";
        chkKiosk.UseVisualStyleBackColor = true;
        //
        // chkAppMode
        //
        chkAppMode.AutoSize = true;
        chkAppMode.Margin = new Padding(0, 0, 16, 0);
        chkAppMode.Name = "chkAppMode";
        chkAppMode.Size = new System.Drawing.Size(55, 19);
        chkAppMode.TabIndex = 1;
        chkAppMode.Text = "app";
        chkAppMode.UseVisualStyleBackColor = true;
        //
        // chkIncognito
        //
        chkIncognito.AutoSize = true;
        chkIncognito.Name = "chkIncognito";
        chkIncognito.Size = new System.Drawing.Size(78, 19);
        chkIncognito.TabIndex = 2;
        chkIncognito.Text = "incognito";
        chkIncognito.UseVisualStyleBackColor = true;
        //
        // layoutProxy
        //
        layoutProxy.ColumnCount = 2;
        layoutProxy.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layoutProxy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layoutProxy.Controls.Add(lblProxy, 0, 0);
        layoutProxy.Controls.Add(txtProxy, 1, 0);
        layoutProxy.Controls.Add(lblBypass, 0, 1);
        layoutProxy.Controls.Add(txtBypass, 1, 1);
        layoutProxy.Dock = DockStyle.Fill;
        layoutProxy.Location = new System.Drawing.Point(11, 49);
        layoutProxy.Margin = new Padding(3, 3, 3, 8);
        layoutProxy.Name = "layoutProxy";
        layoutProxy.RowCount = 2;
        layoutProxy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutProxy.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutProxy.Size = new System.Drawing.Size(498, 58);
        layoutProxy.TabIndex = 1;
        //
        // lblProxy
        //
        lblProxy.AutoSize = true;
        lblProxy.Margin = new Padding(0, 0, 8, 8);
        lblProxy.Name = "lblProxy";
        lblProxy.Size = new System.Drawing.Size(41, 15);
        lblProxy.TabIndex = 0;
        lblProxy.Text = "proxy";
        //
        // txtProxy
        //
        txtProxy.Dock = DockStyle.Fill;
        txtProxy.Margin = new Padding(0, 0, 0, 8);
        txtProxy.Name = "txtProxy";
        txtProxy.Size = new System.Drawing.Size(457, 23);
        txtProxy.TabIndex = 1;
        //
        // lblBypass
        //
        lblBypass.AutoSize = true;
        lblBypass.Margin = new Padding(0, 0, 8, 0);
        lblBypass.Name = "lblBypass";
        lblBypass.Size = new System.Drawing.Size(44, 15);
        lblBypass.TabIndex = 2;
        lblBypass.Text = "bypass";
        //
        // txtBypass
        //
        txtBypass.Dock = DockStyle.Fill;
        txtBypass.Margin = new Padding(0);
        txtBypass.Name = "txtBypass";
        txtBypass.Size = new System.Drawing.Size(457, 23);
        txtBypass.TabIndex = 3;
        //
        // painelTempos
        //
        painelTempos.AutoSize = true;
        painelTempos.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        painelTempos.Dock = DockStyle.Fill;
        painelTempos.FlowDirection = FlowDirection.LeftToRight;
        painelTempos.Location = new System.Drawing.Point(11, 118);
        painelTempos.Margin = new Padding(3, 3, 3, 8);
        painelTempos.Name = "painelTempos";
        painelTempos.Size = new System.Drawing.Size(498, 27);
        painelTempos.TabIndex = 2;
        painelTempos.WrapContents = false;
        painelTempos.Controls.Add(lblTimeout);
        painelTempos.Controls.Add(nudTimeout);
        painelTempos.Controls.Add(lblPostDelay);
        painelTempos.Controls.Add(nudPostLoginDelay);
        //
        // lblTimeout
        //
        lblTimeout.AutoSize = true;
        lblTimeout.Margin = new Padding(0, 0, 8, 0);
        lblTimeout.Name = "lblTimeout";
        lblTimeout.Size = new System.Drawing.Size(81, 15);
        lblTimeout.TabIndex = 0;
        lblTimeout.Text = "Timeout (s)";
        //
        // nudTimeout
        //
        nudTimeout.Margin = new Padding(0, 0, 16, 0);
        nudTimeout.Maximum = new decimal(new int[] { 600, 0, 0, 0 });
        nudTimeout.Name = "nudTimeout";
        nudTimeout.Size = new System.Drawing.Size(80, 23);
        nudTimeout.TabIndex = 1;
        //
        // lblPostDelay
        //
        lblPostDelay.AutoSize = true;
        lblPostDelay.Margin = new Padding(0, 0, 8, 0);
        lblPostDelay.Name = "lblPostDelay";
        lblPostDelay.Size = new System.Drawing.Size(126, 15);
        lblPostDelay.TabIndex = 2;
        lblPostDelay.Text = "PostLogin Delay (s)";
        //
        // nudPostLoginDelay
        //
        nudPostLoginDelay.Margin = new Padding(0);
        nudPostLoginDelay.Maximum = new decimal(new int[] { 600, 0, 0, 0 });
        nudPostLoginDelay.Name = "nudPostLoginDelay";
        nudPostLoginDelay.Size = new System.Drawing.Size(80, 23);
        nudPostLoginDelay.TabIndex = 3;
        //
        // txtPreview
        //
        txtPreview.Dock = DockStyle.Fill;
        txtPreview.Location = new System.Drawing.Point(11, 156);
        txtPreview.Margin = new Padding(3, 3, 3, 0);
        txtPreview.Multiline = true;
        txtPreview.Name = "txtPreview";
        txtPreview.ReadOnly = true;
        txtPreview.ScrollBars = ScrollBars.Vertical;
        txtPreview.Size = new System.Drawing.Size(498, 256);
        txtPreview.TabIndex = 3;
        //
        // ArgsTab
        //
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Controls.Add(layoutPrincipal);
        Name = "ArgsTab";
        Size = new System.Drawing.Size(520, 420);
        ResumeLayout(false);
    }

    #endregion
}
