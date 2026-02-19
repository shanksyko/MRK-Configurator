#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mieruka.App.Forms;

public enum LauncherChoice
{
    None,
    Inventory,
    Configurator,
}

public sealed class LauncherForm : Form
{
    public LauncherChoice SelectedChoice { get; private set; } = LauncherChoice.None;

    public LauncherForm()
    {
        Text = "MRK Configurator";
        ClientSize = new Size(520, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 2,
            Padding = new Padding(24),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        // Title
        var lblTitle = new Label
        {
            Text = "MRK Configurator",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(50, 50, 50),
            Margin = new Padding(0, 0, 0, 8),
        };
        layout.Controls.Add(lblTitle, 0, 0);
        layout.SetColumnSpan(lblTitle, 2);

        // Subtitle
        var lblSubtitle = new Label
        {
            Text = "Selecione o módulo para iniciar:",
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.TopCenter,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16),
        };

        var subtitlePanel = new Panel { Dock = DockStyle.Fill };
        subtitlePanel.Controls.Add(lblSubtitle);

        // Buttons panel
        var btnInventory = CreateModuleButton(
            "Inventário",
            "Gerenciamento de ativos,\ncategorias e movimentações",
            Color.FromArgb(0, 120, 215));
        btnInventory.Click += (_, _) =>
        {
            SelectedChoice = LauncherChoice.Inventory;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnConfigurator = CreateModuleButton(
            "MRK Configurator",
            "Configuração de monitores,\nsites e aplicativos",
            Color.FromArgb(16, 124, 16));
        btnConfigurator.Click += (_, _) =>
        {
            SelectedChoice = LauncherChoice.Configurator;
            DialogResult = DialogResult.OK;
            Close();
        };

        layout.Controls.Add(btnInventory, 0, 1);
        layout.Controls.Add(btnConfigurator, 1, 1);

        // Version label
        var lblVersion = new Label
        {
            Text = "v1.4.0",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 8f),
            Margin = new Padding(0, 8, 0, 0),
        };
        layout.Controls.Add(lblVersion, 0, 2);
        layout.SetColumnSpan(lblVersion, 2);

        Controls.Add(layout);
    }

    private static Button CreateModuleButton(string title, string description, Color accentColor)
    {
        var btn = new Button
        {
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = accentColor,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            Text = $"{title}\n\n{description}",
            Margin = new Padding(8),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, accentColor.R + 30),
            Math.Min(255, accentColor.G + 30),
            Math.Min(255, accentColor.B + 30));
        return btn;
    }
}
