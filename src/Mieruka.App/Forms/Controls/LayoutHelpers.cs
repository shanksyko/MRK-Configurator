using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms.Controls;

internal static class LayoutHelpers
{
    public static void ApplyStandardLayout(Control control)
    {
        if (control is null)
        {
            return;
        }

        if (control is ContainerControl container)
        {
            container.AutoScaleMode = AutoScaleMode.Dpi;
            container.AutoScaleDimensions = new SizeF(96F, 96F);
        }

        if (control is not Form)
        {
            control.Dock = DockStyle.Fill;
        }

        control.Margin = new Padding(8);
    }

    public static TableLayoutPanel CreateStandardTableLayout(int columnCount = 1)
    {
        if (columnCount <= 0)
        {
            columnCount = 1;
        }

        var table = new TableLayoutPanel
        {
            ColumnCount = columnCount,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = true,
            Padding = new Padding(8),
            Margin = new Padding(0),
        };

        if (columnCount == 1)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        }
        else
        {
            var percent = 100F / columnCount;
            for (var i = 0; i < columnCount; i++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));
            }
        }

        return table;
    }
}
