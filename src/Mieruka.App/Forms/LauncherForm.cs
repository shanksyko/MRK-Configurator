#nullable enable
using System;
using System.Drawing;
using System.Reflection;
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
        Text = "Apps";
        ClientSize = new Size(560, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(248, 249, 251);

        // Load app icon
        var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Properties", "app.ico");
        if (System.IO.File.Exists(icoPath))
            Icon = new Icon(icoPath);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 2,
            Padding = new Padding(28),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // row 0: title
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // row 1: subtitle
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // row 2: buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // row 3: version
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

        // Title
        var lblTitle = new Label
        {
            Text = "Apps",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(32, 36, 42),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
        };
        layout.Controls.Add(lblTitle, 0, 0);
        layout.SetColumnSpan(lblTitle, 2);

        // Subtitle
        var lblSubtitle = new Label
        {
            Text = "Selecione o módulo para iniciar:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(96, 96, 96),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        layout.Controls.Add(lblSubtitle, 0, 1);
        layout.SetColumnSpan(lblSubtitle, 2);

        // Buttons panel
        var btnInventory = CreateModuleButton(
            "📦 Inventário",
            "Gerenciamento de ativos,\ncategorias e movimentações",
            Color.FromArgb(0, 120, 215));
        btnInventory.Click += (_, _) =>
        {
            SelectedChoice = LauncherChoice.Inventory;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnConfigurator = CreateModuleButton(
            "⚙ Configurator",
            "Configuração de monitores,\nsites e aplicativos",
            Color.FromArgb(16, 124, 16));
        btnConfigurator.Click += (_, _) =>
        {
            SelectedChoice = LauncherChoice.Configurator;
            DialogResult = DialogResult.OK;
            Close();
        };

        layout.Controls.Add(btnInventory, 0, 2);
        layout.Controls.Add(btnConfigurator, 1, 2);

        // Version label — read from assembly
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version is not null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v?";
        var lblVersion = new Label
        {
            Text = versionText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8f),
            Margin = new Padding(0, 8, 0, 0),
        };
        layout.Controls.Add(lblVersion, 0, 3);
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
            Math.Min(255, accentColor.R + 40),
            Math.Min(255, accentColor.G + 40),
            Math.Min(255, accentColor.B + 40));
        return btn;
    }
}
