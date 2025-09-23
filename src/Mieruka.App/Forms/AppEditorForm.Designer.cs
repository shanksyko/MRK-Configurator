using System.ComponentModel;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;

namespace Mieruka.App.Forms;

partial class AppEditorForm
{
    private IContainer? components = null;
    internal TabControl tabEditor = null!;
    internal TabPage tpGeral = null!;
    internal TabPage tpJanela = null!;
    internal TabPage tpSites = null!;
    internal TabPage tpCiclo = null!;
    internal TabPage tpAvancado = null!;
    internal SitesEditorControl sitesEditorControl = null!;
    internal Button btnSalvar = null!;
    internal Button btnCancelar = null!;

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
        tabEditor = new TabControl();
        tpGeral = new TabPage();
        tpJanela = new TabPage();
        tpSites = new TabPage();
        tpCiclo = new TabPage();
        tpAvancado = new TabPage();
        sitesEditorControl = new SitesEditorControl();
        var footerPanel = new FlowLayoutPanel();
        btnSalvar = new Button();
        btnCancelar = new Button();
        SuspendLayout();
        // 
        // tabEditor
        // 
        tabEditor.Controls.Add(tpGeral);
        tabEditor.Controls.Add(tpJanela);
        tabEditor.Controls.Add(tpSites);
        tabEditor.Controls.Add(tpCiclo);
        tabEditor.Controls.Add(tpAvancado);
        tabEditor.Dock = DockStyle.Fill;
        tabEditor.Location = new System.Drawing.Point(0, 0);
        tabEditor.Name = "tabEditor";
        tabEditor.SelectedIndex = 0;
        tabEditor.Size = new System.Drawing.Size(1040, 640);
        tabEditor.TabIndex = 0;
        // 
        // tpGeral
        // 
        tpGeral.Location = new System.Drawing.Point(4, 24);
        tpGeral.Name = "tpGeral";
        tpGeral.Padding = new Padding(8);
        tpGeral.Size = new System.Drawing.Size(1032, 612);
        tpGeral.TabIndex = 0;
        tpGeral.Text = "Geral";
        tpGeral.UseVisualStyleBackColor = true;
        // 
        // tpJanela
        // 
        tpJanela.Location = new System.Drawing.Point(4, 24);
        tpJanela.Name = "tpJanela";
        tpJanela.Padding = new Padding(8);
        tpJanela.Size = new System.Drawing.Size(1032, 612);
        tpJanela.TabIndex = 1;
        tpJanela.Text = "Janela/Posição";
        tpJanela.UseVisualStyleBackColor = true;
        // 
        // tpSites
        // 
        tpSites.Controls.Add(sitesEditorControl);
        tpSites.Location = new System.Drawing.Point(4, 24);
        tpSites.Name = "tpSites";
        tpSites.Padding = new Padding(8);
        tpSites.Size = new System.Drawing.Size(1032, 612);
        tpSites.TabIndex = 2;
        tpSites.Text = "Sites";
        tpSites.UseVisualStyleBackColor = true;
        // 
        // sitesEditorControl
        // 
        sitesEditorControl.Dock = DockStyle.Fill;
        sitesEditorControl.Location = new System.Drawing.Point(8, 8);
        sitesEditorControl.Name = "sitesEditorControl";
        sitesEditorControl.Size = new System.Drawing.Size(1016, 596);
        sitesEditorControl.TabIndex = 0;
        // 
        // tpCiclo
        // 
        tpCiclo.Location = new System.Drawing.Point(4, 24);
        tpCiclo.Name = "tpCiclo";
        tpCiclo.Padding = new Padding(8);
        tpCiclo.Size = new System.Drawing.Size(1032, 612);
        tpCiclo.TabIndex = 3;
        tpCiclo.Text = "Ciclo";
        tpCiclo.UseVisualStyleBackColor = true;
        // 
        // tpAvancado
        // 
        tpAvancado.Location = new System.Drawing.Point(4, 24);
        tpAvancado.Name = "tpAvancado";
        tpAvancado.Padding = new Padding(8);
        tpAvancado.Size = new System.Drawing.Size(1032, 612);
        tpAvancado.TabIndex = 4;
        tpAvancado.Text = "Avançado";
        tpAvancado.UseVisualStyleBackColor = true;
        // 
        // footerPanel
        // 
        footerPanel.AutoSize = true;
        footerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        footerPanel.Dock = DockStyle.Bottom;
        footerPanel.FlowDirection = FlowDirection.RightToLeft;
        footerPanel.Padding = new Padding(12);
        footerPanel.WrapContents = false;
        footerPanel.Controls.Add(btnSalvar);
        footerPanel.Controls.Add(btnCancelar);
        // 
        // btnSalvar
        // 
        btnSalvar.AutoSize = true;
        btnSalvar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnSalvar.Margin = new Padding(6, 3, 0, 3);
        btnSalvar.Name = "btnSalvar";
        btnSalvar.Size = new System.Drawing.Size(51, 25);
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
        btnCancelar.Size = new System.Drawing.Size(68, 25);
        btnCancelar.TabIndex = 1;
        btnCancelar.Text = "Cancelar";
        btnCancelar.UseVisualStyleBackColor = true;
        btnCancelar.Click += btnCancelar_Click;
        // 
        // AppEditorForm
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1040, 680);
        Controls.Add(tabEditor);
        Controls.Add(footerPanel);
        MinimumSize = new System.Drawing.Size(960, 640);
        Name = "AppEditorForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Editor de Programa";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
