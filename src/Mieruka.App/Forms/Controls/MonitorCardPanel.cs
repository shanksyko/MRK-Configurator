using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Mieruka.App.Forms.Controls;

internal sealed class MonitorCardPanel : WinForms.Panel
{
    private bool _selected;

    public MonitorCardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = SystemColors.Control;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
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

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        base.OnPaint(e);

        var borderColor = _selected
            ? SystemColors.Highlight
            : SystemColors.ControlDark;

        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;

        using var pen = new Drawing.Pen(borderColor, 2f);
        e.Graphics.DrawRectangle(pen, rect);
    }
}
