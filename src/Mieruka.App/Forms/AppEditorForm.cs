using System.Drawing;
using System.Windows.Forms;
using Mieruka.App.Forms.Controls;
using Mieruka.Core.Security;

namespace Mieruka.App.Forms;

public sealed class AppEditorForm : Form
{
    private readonly SitesEditorControl _sitesEditor;

    public AppEditorForm(SecretsProvider secretsProvider)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Editor de Programa";
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
        };

        var generalTab = new TabPage("Geral");
        var windowTab = new TabPage("Janela/Posição");
        var sitesTab = new TabPage("Sites");
        var cycleTab = new TabPage("Ciclo");
        var advancedTab = new TabPage("Avançado");

        _sitesEditor = new SitesEditorControl(secretsProvider);
        sitesTab.Controls.Add(_sitesEditor);

        tabs.TabPages.Add(generalTab);
        tabs.TabPages.Add(windowTab);
        tabs.TabPages.Add(sitesTab);
        tabs.TabPages.Add(cycleTab);
        tabs.TabPages.Add(advancedTab);

        Controls.Add(tabs);
    }
}
