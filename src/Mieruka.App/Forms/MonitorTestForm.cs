using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms;

internal sealed class MonitorTestForm : Form
{
    private readonly Label _messageLabel;

    public MonitorTestForm(string monitorDisplayName)
    {
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        Text = "Teste de Monitor";
        ShowInTaskbar = false;
        TopMost = true;
        MinimumSize = new Size(200, 150);
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;

        var font = SystemFonts.CaptionFont ?? Control.DefaultFont;
        var boldFont = font is null
            ? new Font(FontFamily.GenericSansSerif, 12F, FontStyle.Bold)
            : new Font(font, font.Style | FontStyle.Bold);

        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = boldFont,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Padding = new Padding(16),
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
