#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms;

internal sealed class CoordinateInputDialog : Form
{
    private readonly NumericUpDown _nudX;
    private readonly NumericUpDown _nudY;
    private readonly NumericUpDown _nudWidth;
    private readonly NumericUpDown _nudHeight;

    public CoordinateInputDialog(int x, int y, int width, int height, int maxX, int maxY)
    {
        Text = "Definir coordenadas";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(300, 210);
        ShowInTaskbar = false;
        DoubleBuffered = true;

        var lblX = new Label { Text = "X:", Location = new Point(16, 22), AutoSize = true };
        _nudX = new NumericUpDown
        {
            Location = new Point(80, 20),
            Width = 200,
            Minimum = 0,
            Maximum = Math.Max(0, maxX),
            Value = Math.Clamp(x, 0, Math.Max(0, maxX)),
        };

        var lblY = new Label { Text = "Y:", Location = new Point(16, 54), AutoSize = true };
        _nudY = new NumericUpDown
        {
            Location = new Point(80, 52),
            Width = 200,
            Minimum = 0,
            Maximum = Math.Max(0, maxY),
            Value = Math.Clamp(y, 0, Math.Max(0, maxY)),
        };

        var lblWidth = new Label { Text = "Largura:", Location = new Point(16, 86), AutoSize = true };
        _nudWidth = new NumericUpDown
        {
            Location = new Point(80, 84),
            Width = 200,
            Minimum = 1,
            Maximum = Math.Max(1, maxX + width),
            Value = Math.Max(1, width),
        };

        var lblHeight = new Label { Text = "Altura:", Location = new Point(16, 118), AutoSize = true };
        _nudHeight = new NumericUpDown
        {
            Location = new Point(80, 116),
            Width = 200,
            Minimum = 1,
            Maximum = Math.Max(1, maxY + height),
            Value = Math.Max(1, height),
        };

        var btnOk = new Button
        {
            Text = "OK",
            Location = new Point(114, 160),
            Width = 80,
            DialogResult = DialogResult.OK,
        };

        var btnCancel = new Button
        {
            Text = "Cancelar",
            Location = new Point(200, 160),
            Width = 80,
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            lblX, _nudX,
            lblY, _nudY,
            lblWidth, _nudWidth,
            lblHeight, _nudHeight,
            btnOk, btnCancel,
        });
    }

    public int SelectedX => (int)_nudX.Value;
    public int SelectedY => (int)_nudY.Value;
    public int SelectedWidth => (int)_nudWidth.Value;
    public int SelectedHeight => (int)_nudHeight.Value;
}
