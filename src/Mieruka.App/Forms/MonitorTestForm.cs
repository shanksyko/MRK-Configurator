using System.Drawing;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Forms;

internal sealed class MonitorTestForm : WinForms.Form
{
    private readonly WinForms.Label _messageLabel;

    public MonitorTestForm(string monitorDisplayName)
    {
        StartPosition = WinForms.FormStartPosition.Manual;
        FormBorderStyle = WinForms.FormBorderStyle.SizableToolWindow;
        Text = "Teste de Monitor";
        ShowInTaskbar = false;
        TopMost = true;
        MinimumSize = new Drawing.Size(200, 150);
        BackColor = Drawing.Color.FromArgb(32, 32, 32);
        ForeColor = Drawing.Color.White;
        DoubleBuffered = true;

        var font = SystemFonts.CaptionFont ?? WinForms.Control.DefaultFont;
        var boldFont = font is null
            ? new Drawing.Font(FontFamily.GenericSansSerif, 12F, FontStyle.Bold)
            : new Drawing.Font(font, font.Style | FontStyle.Bold);

        _messageLabel = new WinForms.Label
        {
            Dock = WinForms.DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = boldFont,
            ForeColor = Drawing.Color.White,
            BackColor = Drawing.Color.Transparent,
            Padding = new WinForms.Padding(16),
        };

        Controls.Add(_messageLabel);

        UpdateMonitorName(monitorDisplayName);
    }

    public void UpdateMonitorName(string monitorDisplayName)
    {
        var display = string.IsNullOrWhiteSpace(monitorDisplayName)
            ? "Monitor"
            : monitorDisplayName;

        _messageLabel.Text = $"Teste de posicionamento\n{display}\nUse 'Parar' para fechar.";
    }
}
