using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Services;

/// <summary>
/// Provides light/dark theme switching for WinForms controls.
/// </summary>
public static class ThemeManager
{
    public enum AppTheme
    {
        Light,
        Dark,
    }

    private static readonly Color DarkBack = Color.FromArgb(30, 30, 30);
    private static readonly Color DarkFore = Color.FromArgb(220, 220, 220);
    private static readonly Color DarkControl = Color.FromArgb(45, 45, 45);
    private static readonly Color DarkBorder = Color.FromArgb(60, 60, 60);

    /// <summary>
    /// Gets or sets the currently active theme.
    /// </summary>
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    /// <summary>
    /// Applies the specified theme to a form and all its child controls recursively.
    /// </summary>
    public static void ApplyTheme(Control root, AppTheme theme)
    {
        if (root is null) return;

        CurrentTheme = theme;

        root.SuspendLayout();
        try
        {
            if (theme == AppTheme.Light)
            {
                ApplyLightTheme(root);
            }
            else
            {
                ApplyDarkTheme(root);
            }
        }
        finally
        {
            root.ResumeLayout(true);
        }
    }

    private static void ApplyDarkTheme(Control control)
    {
        switch (control)
        {
            case DataGridView dgv:
                dgv.BackgroundColor = DarkBack;
                dgv.DefaultCellStyle.BackColor = DarkControl;
                dgv.DefaultCellStyle.ForeColor = DarkFore;
                dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
                dgv.DefaultCellStyle.SelectionForeColor = Color.White;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = DarkBorder;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = DarkFore;
                dgv.EnableHeadersVisualStyles = false;
                dgv.GridColor = DarkBorder;
                break;

            case MenuStrip menu:
                menu.BackColor = DarkControl;
                menu.ForeColor = DarkFore;
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
                ApplyToolStripItems(menu.Items);
                break;

            case StatusStrip ss:
                ss.BackColor = DarkControl;
                ss.ForeColor = DarkFore;
                foreach (ToolStripItem item in ss.Items)
                {
                    item.ForeColor = DarkFore;
                    item.BackColor = DarkControl;
                }
                break;

            case ToolStrip ts:
                ts.BackColor = DarkControl;
                ts.ForeColor = DarkFore;
                ts.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
                ApplyToolStripItems(ts.Items);
                break;

            case ListView lv:
                lv.BackColor = DarkControl;
                lv.ForeColor = DarkFore;
                break;

            case TreeView tv:
                tv.BackColor = DarkControl;
                tv.ForeColor = DarkFore;
                break;

            case TextBox tb:
                tb.BackColor = DarkControl;
                tb.ForeColor = DarkFore;
                break;

            case ComboBox cb:
                cb.BackColor = DarkControl;
                cb.ForeColor = DarkFore;
                break;

            case NumericUpDown nud:
                nud.BackColor = DarkControl;
                nud.ForeColor = DarkFore;
                break;

            case GroupBox gb:
                gb.ForeColor = DarkFore;
                gb.BackColor = DarkBack;
                break;

            case TabControl tc:
                tc.BackColor = DarkBack;
                tc.ForeColor = DarkFore;
                break;

            case TabPage tp:
                tp.BackColor = DarkBack;
                tp.ForeColor = DarkFore;
                break;

            case Button btn:
                btn.BackColor = DarkControl;
                btn.ForeColor = DarkFore;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = DarkBorder;
                break;

            case Label lbl:
                lbl.ForeColor = DarkFore;
                break;

            case CheckBox chk:
                chk.ForeColor = DarkFore;
                break;

            default:
                control.BackColor = DarkBack;
                control.ForeColor = DarkFore;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyDarkTheme(child);
        }
    }

    private static void ApplyLightTheme(Control control)
    {
        switch (control)
        {
            case DataGridView dgv:
                dgv.BackgroundColor = SystemColors.Window;
                dgv.DefaultCellStyle.BackColor = SystemColors.Window;
                dgv.DefaultCellStyle.ForeColor = SystemColors.ControlText;
                dgv.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                dgv.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
                dgv.EnableHeadersVisualStyles = true;
                dgv.GridColor = SystemColors.ControlDark;
                break;

            case MenuStrip menu:
                menu.BackColor = SystemColors.Control;
                menu.ForeColor = SystemColors.ControlText;
                menu.Renderer = new ToolStripProfessionalRenderer();
                ApplyToolStripItemsLight(menu.Items);
                break;

            case StatusStrip ss:
                ss.BackColor = SystemColors.Control;
                ss.ForeColor = SystemColors.ControlText;
                foreach (ToolStripItem item in ss.Items)
                {
                    item.ForeColor = SystemColors.ControlText;
                    item.BackColor = SystemColors.Control;
                }
                break;

            case ToolStrip ts:
                ts.BackColor = SystemColors.Control;
                ts.ForeColor = SystemColors.ControlText;
                ts.Renderer = new ToolStripProfessionalRenderer();
                ApplyToolStripItemsLight(ts.Items);
                break;

            case ListView lv:
                lv.BackColor = SystemColors.Window;
                lv.ForeColor = SystemColors.WindowText;
                break;

            case TreeView tv:
                tv.BackColor = SystemColors.Window;
                tv.ForeColor = SystemColors.WindowText;
                break;

            case TextBox tb:
                tb.BackColor = SystemColors.Window;
                tb.ForeColor = SystemColors.WindowText;
                break;

            case ComboBox cb:
                cb.BackColor = SystemColors.Window;
                cb.ForeColor = SystemColors.WindowText;
                break;

            case NumericUpDown nud:
                nud.BackColor = SystemColors.Window;
                nud.ForeColor = SystemColors.WindowText;
                break;

            case GroupBox gb:
                gb.ForeColor = SystemColors.ControlText;
                gb.BackColor = SystemColors.Control;
                break;

            case TabControl tc:
                tc.BackColor = SystemColors.Control;
                tc.ForeColor = SystemColors.ControlText;
                break;

            case TabPage tp:
                tp.BackColor = SystemColors.Control;
                tp.ForeColor = SystemColors.ControlText;
                break;

            case Button btn:
                btn.BackColor = SystemColors.Control;
                btn.ForeColor = SystemColors.ControlText;
                btn.FlatStyle = FlatStyle.Standard;
                btn.UseVisualStyleBackColor = true;
                break;

            case Label lbl:
                lbl.ForeColor = SystemColors.ControlText;
                break;

            case CheckBox chk:
                chk.ForeColor = SystemColors.ControlText;
                break;

            default:
                control.BackColor = SystemColors.Control;
                control.ForeColor = SystemColors.ControlText;
                break;
        }

        foreach (Control child in control.Controls)
        {
            ApplyLightTheme(child);
        }
    }

    private static void ApplyToolStripItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = DarkFore;
            item.BackColor = DarkControl;

            if (item is ToolStripDropDownItem dropdown)
            {
                ApplyToolStripItems(dropdown.DropDownItems);
            }
        }
    }

    private static void ApplyToolStripItemsLight(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = SystemColors.ControlText;
            item.BackColor = SystemColors.Control;

            if (item is ToolStripDropDownItem dropdown)
            {
                ApplyToolStripItemsLight(dropdown.DropDownItems);
            }
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => DarkControl;
        public override Color MenuStripGradientEnd => DarkControl;
        public override Color MenuItemSelected => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(60, 60, 60);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(60, 60, 60);
        public override Color MenuItemBorder => DarkBorder;
        public override Color MenuBorder => DarkBorder;
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(70, 70, 70);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(70, 70, 70);
        public override Color ToolStripDropDownBackground => DarkControl;
        public override Color ImageMarginGradientBegin => DarkControl;
        public override Color ImageMarginGradientMiddle => DarkControl;
        public override Color ImageMarginGradientEnd => DarkControl;
        public override Color SeparatorDark => DarkBorder;
        public override Color SeparatorLight => DarkBorder;
    }
}
