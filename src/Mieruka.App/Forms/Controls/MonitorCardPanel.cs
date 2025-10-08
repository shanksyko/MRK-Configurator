using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls;

internal sealed class MonitorCardPanel : Panel
{
    private bool _selected;

    public MonitorCardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = SystemColors.Control;
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var borderColor = _selected
            ? SystemColors.Highlight
            : SystemColors.ControlDark;

        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var pen = new Pen(borderColor, 2f);
        e.Graphics.DrawRectangle(pen, rect);
    }
}
